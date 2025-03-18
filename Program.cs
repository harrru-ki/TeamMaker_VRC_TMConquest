using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using BuildSoft.VRChat.Osc.Chatbox;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.Globalization;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8; // UTF-8 を設定

string logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "..", "LocalLow", "VRChat", "VRChat"
        );
string jsonDataPath = Path.Combine(logDir, "TeamMakerMarusav.json");
var logFiles = Directory.GetFiles(logDir, "output_log_*.txt");

if (logFiles.Length == 0)
{
    Console.WriteLine("ログファイルが見つかりません。");
    return;
}

string latestLogFile = logFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
List<string> logLines = ReadLogFileSafely(latestLogFile);
logLines.Reverse();
List<string> filteredLogs = new List<string>();

foreach (var line in logLines)
{
    if (line.Contains("[Behaviour] Finished entering world."))
        break;
    if (line.Contains("[Behaviour] OnPlayerJoined ") || line.Contains("[Behaviour] OnPlayerLeft "))
        filteredLogs.Add(line);
}

var joinedPlayers = filteredLogs
    .Where(line => line.Contains("[Behaviour] OnPlayerJoined"))
    .Select(line => Regex.Replace(line, @".*\[Behaviour\] OnPlayerJoined ", ""))
    .Select(line => Regex.Replace(line, @"\(usr_.*\)$", ""))
    .OrderBy(name => name)
    .ToList();

var leftPlayers = filteredLogs
    .Where(line => line.Contains("[Behaviour] OnPlayerLeft"))
    .Select(line => Regex.Replace(line, @".*\[Behaviour\] OnPlayerLeft ", ""))
    .Select(line => Regex.Replace(line, @"\(usr_.*\)$", ""))
    .OrderBy(name => name)
    .ToList();

Console.WriteLine("-------joinedPlayers------------");
Console.WriteLine(string.Join("\n", joinedPlayers));
Console.WriteLine("-------leftPlayers--------------");
Console.WriteLine(string.Join("\n", leftPlayers));

List<string> currentPlayers = new List<string>(joinedPlayers);
foreach (var player in leftPlayers)
{
    currentPlayers.Remove(player);
}



JsonData jsonData = ReadJsoData(jsonDataPath);
List<string> excludedPlayers = jsonData.excludedPlayers;
while (true)
{
    Console.WriteLine("チーム分け除外プレイヤー(excludePlayers)に追加/削除したいプレイヤーの番号を入力後、Enterを入力してください。番号無しEnterのみ入力で確定します...");
    Console.WriteLine("-------currentPlayers----------");
    for (int i = 0; i < currentPlayers.Count; i++)
    {
        Console.WriteLine($"[{i}] {currentPlayers[i]}");
    }
    Console.WriteLine("{0} users in this instance.", currentPlayers.Count);
    Console.WriteLine("-------excludedPlayers----------");
    for (int i = 0; i < excludedPlayers.Count; i++)
    {
        Console.WriteLine($"[{i + currentPlayers.Count}] {excludedPlayers[i]}");
    }
    Console.WriteLine("{0} users excluded.", excludedPlayers.Count);

    string input = Console.ReadLine()?.Trim(); // 入力を取得し、前後の空白を削除
    if (input.Equals("", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }
    // 数字が入力された場合、iに加算
    if (int.TryParse(input, out int num))
    {
        if (0 <= num && num < currentPlayers.Count) {
            string playerExcluded = currentPlayers[num];
            Console.WriteLine("---> {0} to be excluded", playerExcluded);
            excludedPlayers.Add(playerExcluded);
            excludedPlayers = excludedPlayers.Distinct().OrderBy(p => p).ToList();
        }
        else if  (currentPlayers.Count <= num && num < (currentPlayers.Count+excludedPlayers.Count))
        {
            string playerIncluded = excludedPlayers[num - currentPlayers.Count];
            Console.WriteLine("---> {0} to be included", playerIncluded);
            excludedPlayers.Remove(playerIncluded);
        }
        else
        {
            Console.WriteLine("---> 無効な入力です。再入力してください...");
        }
    }
    else
    {
        // 無効な入力の場合は再入力を促す
        Console.WriteLine("---> 無効な入力です。再入力してください...");
    }
}
SaveJsonData(jsonData, jsonDataPath);

List<string> targetPlayers = new List<string>(currentPlayers);
foreach (var player in excludedPlayers)
{
    targetPlayers.Remove(player);
}
var (redTeamPlayers, blueTeamPlayers) = SplitListRandomly(targetPlayers);
redTeamPlayers = redTeamPlayers.Select(p => p + ": 赤チーム\n").OrderBy(p => p).ToList();
blueTeamPlayers = blueTeamPlayers.Select(p => p + ": 青チーム\n").OrderBy(p => p).ToList();
var excludedTeamPlayers = excludedPlayers.Select(p => p + ": 見学\n").OrderBy(p => p).ToList();

List<string> redBlueTeamPlayers = new List<string>(redTeamPlayers);
redBlueTeamPlayers.Add(string.Format("\n"));
redBlueTeamPlayers = redBlueTeamPlayers.Concat(blueTeamPlayers).ToList();
redBlueTeamPlayers.Add(string.Format("\n"));
redBlueTeamPlayers = redBlueTeamPlayers.Concat(excludedTeamPlayers).ToList();
int total_player_count = redTeamPlayers.Count + blueTeamPlayers.Count + excludedTeamPlayers.Count;
redBlueTeamPlayers.Add(string.Format("赤:{0}, 青:{1}, 見学{2} -> 合計 {3}", redTeamPlayers.Count, blueTeamPlayers.Count, excludedTeamPlayers.Count, total_player_count));

const int maxLines = 7; // use 7 lines for safety instead of 9 lines in VRChat docs
const int maxChar = 120; // use 120 characters for safety instead of 144 characters  in VRChat docs
int r = 0;
string s = "";
List<string> msgs = new List<string>();
foreach (var p in redBlueTeamPlayers)
{
    if ((r + 1) > maxChar || (s + p).Count() > maxChar)
    {
        msgs.Add(s);
        Console.WriteLine(s);
        r = 0;
        s = "";
    }
    r += 1;
    s += p;
}
if (s != "")
{
    msgs.Add(s);
    Console.WriteLine(s);
}

Task.Run(async () =>
{
    while (true)
    {
        foreach (var msg in msgs)
        {
            OscChatbox.SetIsTyping(false);
            await Task.Delay(1000);
            OscChatbox.SendMessage(msg, true);
            await Task.Delay(4000);
        }
    }
});
Console.WriteLine("キーを押すとプログラムが終了します...");
Console.ReadKey(); // 1回キーが押されるまで待機
Console.WriteLine("プログラムを終了します。");
OscChatbox.SendMessage("", true, true);
static List<string> ReadLogFileSafely(string filePath)
{
    List<string> lines = new List<string>();

    try
    {
        // 他のプロセスが開いていても読めるようにする
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader sr = new StreamReader(fs))
        {
            while (!sr.EndOfStream)
            {
                lines.Add(sr.ReadLine());
            }
        }
    }
    catch (IOException ex)
    {
        Console.WriteLine("ファイルの読み取りに失敗しました: " + ex.Message);
    }

    return lines;
}
static (List<string>, List<string>) SplitListRandomly(List<string> list)
{
    Random rand = new Random();

    // 1. シャッフル
    List<string> shuffled = list.OrderBy(_ => rand.Next()).ToList();

    // 2. 半分に分割
    int halfSize = (int)Math.Ceiling(shuffled.Count / 2.0);
    List<string> group1 = shuffled.Take(halfSize).ToList();
    List<string> group2 = shuffled.Skip(halfSize).ToList();

    return (group1, group2);
}

static JsonData ReadJsoData(string filePath)
{
    JsonData jsonData = new JsonData(); 

    if (File.Exists(filePath))
    {
        try
        {
            string json = File.ReadAllText(filePath);
            jsonData = JsonSerializer.Deserialize<JsonData>(json);
            Console.WriteLine("読込完了: {0}", filePath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"ファイルの読み込み中にエラーが発生しました: {e.Message}");
        }
    }

    return jsonData;
}
static void SaveJsonData(JsonData data, string filePath)
{
    try
    {
        // JSONへシリアライズ（整形あり）
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        // ファイルに書き込み
        File.WriteAllText(filePath, json);
        Console.WriteLine("保存完了: {0}", filePath);
    }
    catch (Exception e)
    {
        Console.WriteLine($"ファイルの保存中にエラーが発生しました: {e.Message}");
    }
}
class JsonData
{
    public List<string> excludedPlayers { get; set; } = new List<string>();
}
