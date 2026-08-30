using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MHZMultiplayerInstaller
{
    // one-click installer. finds MH-Zombie on any drive, downloads BepInEx and
    // the prebuilt mod dll, drops them in the right places. no SDK, no build,
    // nothing for the player to edit.
    internal static class Program
    {
        const string BepInExUrl =
            "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_x64_5.4.23.2.zip";
        const string ModDllUrl =
            "https://github.com/DevTeam6Rabbit/MHZMultiplayer/releases/latest/download/MHZombieMultiplayer.dll";

        static readonly string[] SteamRoots =
        {
            @"Program Files (x86)\Steam\steamapps\common\MH-Zombie",
            @"Program Files\Steam\steamapps\common\MH-Zombie",
            @"SteamLibrary\steamapps\common\MH-Zombie",
            @"Steam\steamapps\common\MH-Zombie",
            @"Games\Steam\steamapps\common\MH-Zombie",
        };

        static async Task<int> Main()
        {
            Console.Title = "MHZ Multiplayer Installer";
            Line("=====================================");
            Line("   MHZ Multiplayer - installer");
            Line("=====================================");
            Console.WriteLine();

            try
            {
                string gameDir = FindGame() ?? AskForGame();
                if (gameDir == null) return Fail("No game folder given.");

                Line($"Installing to: {gameDir}");
                Console.WriteLine();

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "MHZMultiplayerInstaller");
                http.Timeout = TimeSpan.FromMinutes(5);

                if (File.Exists(Path.Combine(gameDir, "winhttp.dll")))
                {
                    Line("BepInEx already installed, skipping.");
                }
                else
                {
                    Line("Downloading BepInEx...");
                    byte[] zip = await http.GetByteArrayAsync(BepInExUrl);
                    string tmp = Path.Combine(Path.GetTempPath(), "bepinex_mhz.zip");
                    File.WriteAllBytes(tmp, zip);
                    Line("Extracting BepInEx...");
                    ZipFile.ExtractToDirectory(tmp, gameDir, true);
                    File.Delete(tmp);
                }

                string plugins = Path.Combine(gameDir, "BepInEx", "plugins");
                Directory.CreateDirectory(plugins);

                Line("Downloading the mod...");
                byte[] dll = await http.GetByteArrayAsync(ModDllUrl);
                File.WriteAllBytes(Path.Combine(plugins, "MHZombieMultiplayer.dll"), dll);

                Console.WriteLine();
                Ok("Install complete.");
                Line("Launch the game from Steam, then press F8 in a level.");
                Line("F9 hosts a lobby, F10 leaves it.");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }

            Console.WriteLine();
            Line("Press any key to close.");
            Console.ReadKey(true);
            return 0;
        }

        // the game exe sits in a "MHZ Build 13.1" style subfolder
        static string Resolve(string mhZombieFolder)
        {
            if (!Directory.Exists(mhZombieFolder)) return null;
            if (File.Exists(Path.Combine(mhZombieFolder, "MHZ.exe"))) return mhZombieFolder;
            return Directory.GetDirectories(mhZombieFolder)
                            .FirstOrDefault(d => File.Exists(Path.Combine(d, "MHZ.exe")));
        }

        static string FindGame()
        {
            Line("Looking for MH-Zombie...");
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                foreach (string rel in SteamRoots)
                {
                    string hit = Resolve(Path.Combine(drive.Name, rel));
                    if (hit == null) continue;
                    Ok($"Found: {hit}");
                    Console.Write("  Use this folder? [Y/n]: ");
                    string answer = (Console.ReadLine() ?? "").Trim();
                    if (answer.Equals("n", StringComparison.OrdinalIgnoreCase)) return null;
                    return hit;
                }
            }
            Line("Couldn't find it automatically.");
            return null;
        }

        static string AskForGame()
        {
            while (true)
            {
                Console.WriteLine();
                Line("Paste your MH-Zombie folder path and press Enter.");
                Line("(Steam: right-click the game > Manage > Browse local files)");
                Line("Leave blank to cancel.");
                Console.Write("  Path: ");
                string input = (Console.ReadLine() ?? "").Trim().Trim('"');
                if (input.Length == 0) return null;

                if (input.EndsWith("MHZ.exe", StringComparison.OrdinalIgnoreCase))
                    input = Path.GetDirectoryName(input);

                string hit = Resolve(input);
                if (hit != null) { Ok($"Found: {hit}"); return hit; }
                Warn("No MHZ.exe there. Try again.");
            }
        }

        static void Line(string s) => Console.WriteLine("  " + s);
        static void Ok(string s) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  " + s); Console.ResetColor(); }
        static void Warn(string s) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine("  " + s); Console.ResetColor(); }

        static int Fail(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("  FAILED: " + message);
            Console.ResetColor();
            Console.WriteLine("  Press any key to close.");
            Console.ReadKey(true);
            return 1;
        }
    }
}
