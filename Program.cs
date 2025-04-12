using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildSoft.VRChat.Osc.Chatbox;

Console.OutputEncoding = Encoding.UTF8;

string logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "..", "LocalLow", "VRChat", "VRChat"
);
string jsonDataPath = Path.Combine(logDir, "TeamMakerMarusav.json");

var logFiles = Directory.GetFiles(logDir, "output_log_*.txt");
if (!logFiles.Any())
{
    Console.WriteLine("ログファイルが見つかりません。");
    return;
}

string latestLogFile = logFiles.OrderByDescending(File.GetLastWriteTime).First();
List<string> logLines = ReadLogFileSafely(latestLogFile);
logLines.Reverse();

List<string> filteredLogs = logLines
    .TakeWhile(line => !line.Contains("[Behaviour] Finished entering world."))
    .Where(line => line.Contains("[Behaviour] OnPlayerJoined ") || line.Contains("[Behaviour] OnPlayerLeft "))
    .ToList();

var joinedPlayers = ExtractPlayerNames(filteredLogs, "OnPlayerJoined");
var leftPlayers = ExtractPlayerNames(filteredLogs, "OnPlayerLeft");

// 下記のコードだと、あるPlayerがワールドに入退出を繰り返したケースでバグとなる。
// - `var currentPlayers = joinedPlayers.Except(leftPlayers).ToList();` 
// 例えばワールドにJoinした後Leftし、再度Joinした場合、curentPlayersから除外されてしまう。
// 下記のコードはそのようなケースでも正しく動作する。
List<string> currentPlayers = new List<string>(joinedPlayers);
foreach (var player in leftPlayers)
{
    currentPlayers.Remove(player);
}

var jsonOptions = new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default,
    WriteIndented = true,
};
var result = ReadJsonData(jsonDataPath, jsonOptions);
var jsonData = result.data;
var readJsonMessage = result.message;
var excludedPlayers = jsonData.excludedPlayers;

UpdateExcludedPlayers(ref excludedPlayers, currentPlayers, readJsonMessage);

var targetPlayers = currentPlayers.Except(excludedPlayers).ToList();
var (redTeam, blueTeam) = SplitListRandomly(targetPlayers);
var messages = FormatTeamMessages(redTeam, blueTeam, excludedPlayers);

var saveJsonMessage = SaveJsonData(jsonData, jsonDataPath, jsonOptions);
Console.Clear();
Console.WriteLine(saveJsonMessage);
Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~ チーム分け結果 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
messages.ForEach(Console.WriteLine);
Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

Task.Run(async () =>
{
    while (true)
    {
        foreach (var msg in messages)
        {
            OscChatbox.SetIsTyping(false);
            await Task.Delay(1000);
            OscChatbox.SendMessage(msg, true);
            await Task.Delay(5000);
        }
    }
});

Console.WriteLine("[VRChatのChatBoxに送信中]:");
Console.WriteLine(" - 120文字ずつ送信しています...チーム分け結果を確認してください...");
Console.WriteLine(" - キーを押すとプログラムが終了します...");
Console.Write("-> ");
Console.ReadKey(); // 1回キーが押されるまで待機
Console.WriteLine("プログラムを終了します。");
OscChatbox.SendMessage("", true, true);

// ------------------- Helper Methods ------------------------

static List<string> ReadLogFileSafely(string filePath)
{
    try
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd().Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    }
    catch (IOException ex)
    {
        Console.WriteLine("ファイルの読み取りに失敗しました: " + ex.Message);
        return new();
    }
}

static List<string> ExtractPlayerNames(List<string> lines, string keyword)
{
    return lines
        .Where(line => line.Contains(@$"[Behaviour] {keyword} "))
        .Select(line => Regex.Replace(line, @$".*\[Behaviour\] {keyword} ", ""))
        .Select(line => Regex.Replace(line, @"\(usr_.*\).*$", ""))
        .OrderBy(name => name)
        .ToList();
}

static (List<string>, List<string>) SplitListRandomly(List<string> list)
{
    var rand = new Random();
    var shuffled = list.OrderBy(_ => rand.Next()).ToList();
    int half = (int)Math.Ceiling(shuffled.Count / 2.0);
    return (shuffled.Take(half).ToList(), shuffled.Skip(half).ToList());
}

static void UpdateExcludedPlayers(ref List<string> excludedPlayers, List<string> currentPlayers, string headerMessage)
{
    while (true)
    { 
        Console.Clear();
        Console.WriteLine(headerMessage);
        Console.WriteLine("~~~~~~~~~~~~~~~~~~~ 現在、ワールドにいるプレイヤー ~~~~~~~~~~~~~~~~~~~~~");
        for (int i = 0; i < currentPlayers.Count; i++)
        {
            Console.WriteLine($"[{i}] {currentPlayers[i]}");
        }
        Console.WriteLine("-> 合計 {0} 名", currentPlayers.Count);
        Console.WriteLine("~~~~~~~~ 見学プレイヤー(チーム分けから除外するプレイヤー) ~~~~~~~~~~~~~~");
        for (int i = 0; i < excludedPlayers.Count; i++)
        {
            Console.WriteLine($"[{i + currentPlayers.Count}] {excludedPlayers[i]}");
        }
        Console.WriteLine("-> 合計 {0} 名", excludedPlayers.Count);
        Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
        Console.WriteLine("");
        Console.WriteLine("[見学プレイヤーの追加/削除]:");
        Console.WriteLine(" - 見学プレイヤーに追加/削除したい場合・・・プレイヤーの番号を入力後、Enterを入力してください。");
        Console.WriteLine(" - 見学プレイヤーを上記で確定したい場合・・・Enterを入力してください。チーム分けを実行します...");

        var tail_index = currentPlayers.Count + excludedPlayers.Count - 1;
        while (true)
        {
            Console.Write("-> ");
            string input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            if (int.TryParse(input, out int index))
            {
                if (0 <= index && index < currentPlayers.Count)
                {
                    var player = currentPlayers[index];
                    Console.WriteLine($"---> {player} を見学プレイヤーに追加します...");
                    Thread.Sleep(1000);
                    excludedPlayers.Add(player);
                    excludedPlayers = excludedPlayers.Distinct().OrderBy(p => p).ToList();
                    break;
                }
                else if (index >= currentPlayers.Count && index < currentPlayers.Count + excludedPlayers.Count)
                {
                    var player = excludedPlayers[index - currentPlayers.Count];
                    Console.WriteLine($"---> {player} を見学プレイヤーから削除します...");
                    Thread.Sleep(1000);
                    excludedPlayers.Remove(player);
                    break;
                }
                else
                {
                    Console.WriteLine($"---> 無効な入力です。0以上{tail_index}以下の数字を入力してください。再入力してください...");
                }
            }
            else
            {
                Console.WriteLine($"---> 無効な入力です。0以上{tail_index}以下の数字を入力してください。再入力してください...");
            }
        }
    }
}

static List<string> FormatTeamMessages(List<string> red, List<string> blue, List<string> excluded)
{
    var result = new List<string>();
    var all = red.Select(p => p + ": 赤チーム\n").OrderBy(p => p)
        .Concat(blue.Select(p => p + ": 青チーム\n").OrderBy(p => p))
        .Concat(excluded.Select(p => p + ": 見学\n").OrderBy(p => p))
        .ToList();

    all.Add($"赤:{red.Count}, 青:{blue.Count}, 見学:{excluded.Count} → 合計 {red.Count + blue.Count + excluded.Count}");

    const int maxLines = 7; // use 7 lines for safety instead of 9 lines in VRChat docs
    const int maxChar = 120; // use 120 characters for safety instead of 144 characters in VRChat docs

    int lineCount = 0;
    string chunk = "";

    foreach (var line in all)
    {
        if (lineCount + 1 > maxLines || (chunk + line).Length > maxChar)
        {
            result.Add(chunk);
            lineCount = 0;
            chunk = "";
        }
        lineCount++;
        chunk += line;
    }
    if (!string.IsNullOrWhiteSpace(chunk)) result.Add(chunk);

    return result;
}

static (JsonData data, string message) ReadJsonData(string filePath, JsonSerializerOptions options)
{
    try
    {
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<JsonData>(json, options);
            var message = $"読込完了: {filePath}";
            Console.WriteLine(message);
            return (data ?? new JsonData(), message);
        }
    }
    catch (Exception e)
    {
        var message = $"ファイルの読み込み中にエラーが発生しました: {e.Message}";
        Console.WriteLine(message);
        return (new JsonData(), message);
    }
    return (new JsonData(), "Error, Unreachable Code");
}

static string  SaveJsonData(JsonData data, string filePath, JsonSerializerOptions options)
{
    try
    {
        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(filePath, json);
        var message = $"保存完了: {filePath}";
        Console.WriteLine(message);
        return message;
    }
    catch (Exception e)
    {
        var message = $"保存エラー: {e.Message}";
        Console.WriteLine(message);
        return message;
    }
    return "Error, Unreachable Code";
}

public class JsonData
{
    public List<string> excludedPlayers { get; set; } = new();
}

[JsonSerializable(typeof(JsonData))]
public partial class MyJsonContext : JsonSerializerContext { }
