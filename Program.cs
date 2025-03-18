using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using BuildSoft.VRChat.Osc.Chatbox;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.Globalization;

Console.OutputEncoding = Encoding.UTF8; // UTF-8 を設定

string logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "..", "LocalLow", "VRChat", "VRChat"
        );
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

Console.WriteLine("-------currentPlayers----------");
Console.WriteLine(string.Join("\n", currentPlayers));
Console.WriteLine("{0} users in this instance.", currentPlayers.Count);

var (redTeamPlayers, blueTeamPlayers) = SplitListRandomly(currentPlayers);
redTeamPlayers = redTeamPlayers.Select(p => p + ": 赤チーム\n").OrderBy(p => p).ToList();
blueTeamPlayers = blueTeamPlayers.Select(p => p + ": 青チーム\n").OrderBy(p => p).ToList();

List<string> redBlueTeamPlayers = new List<string>(redTeamPlayers);
redBlueTeamPlayers.Add(string.Format("\n"));
redBlueTeamPlayers = redBlueTeamPlayers.Concat(blueTeamPlayers).ToList();
redBlueTeamPlayers.Add(string.Format("赤チーム: {0} users, 青チーム: {1} users", redTeamPlayers.Count, blueTeamPlayers.Count));

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
