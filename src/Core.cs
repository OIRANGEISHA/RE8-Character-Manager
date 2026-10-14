using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RE8Characters
{
    public static class HighHeelAssets
    {
        public const string Container = "natives/stm/sound/sound_asset/snd_containerlist/pl/snd_containerlist_pl1000.wcc.2";
        public const string Events = "natives/stm/sound/sound_asset/snd_eventlist/pl/snd_eventlist_pl1000_hl_foot.wel.11";
        public const string Bank = "natives/stm/sound/wwise/pl1000_hl_foot.bnk.2.x64";
        public const string NativeBank = "natives/stm/sound/wwise/pl1000_mt_foot.bnk.2.x64";
        // Keep v0.1 paths valid so existing journals can restore them on upgrade.
        public static readonly string[] LegacyPaths = { Container, Events, Bank };
        public static readonly string[] Paths = { Container, Events, Bank, NativeBank };
    }

    public static class BreathAssets
    {
        public const string EnglishBank = "natives/stm/sound/wwise/pl1000_v.bnk.2.x64.en";
        public static readonly string[] Languages = { "en", "zhcn", "ja", "de", "fr", "it", "es", "ptbr", "ru" };
        public static readonly string[] Paths = Languages.Select(language =>
            "natives/stm/sound/wwise/pl1000_v.bnk.2.x64." + language).ToArray();
        public static bool Contains(string path) { return Paths.Contains(path, StringComparer.OrdinalIgnoreCase); }
    }

    public sealed class ModPackage
    {
        public string Id;
        public string Name;
        public string Archive;
        public string Group;
        public override string ToString() { return Name; }
    }

    public sealed class FileRecord
    {
        public string Relative;
        public string Stage;
        public string Backup;
        public bool OriginalExists;
        public string OriginalHash;
        public string InstalledHash;
        public long Length;
    }

    public sealed class HashPatch
    {
        public int Index;
        public string Original;
    }

    public sealed class PakRecord
    {
        public string Relative;
        public long Length;
        public string Header;
        public string OriginalTocHash;
        public List<HashPatch> Patches = new List<HashPatch>();
    }

    public sealed class InstallState
    {
        public int Format = 1;
        public string GameRoot;
        public string Session;
        public string Status;
        public string CreatedUtc;
        public List<ModPackage> Mods = new List<ModPackage>();
        public List<FileRecord> Files = new List<FileRecord>();
        public List<PakRecord> Paks = new List<PakRecord>();
    }

    public sealed class Settings
    {
        public string GameRoot = "";
    }

    public static class Codec
    {
        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        }
        public static string Json(object value) { return Serializer().Serialize(value); }
        public static T Read<T>(string path) { return Serializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static void Save(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Json(value));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        public static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string FileHash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static uint MurmurUtf16(string name)
        {
            unchecked
            {
                byte[] bytes = Encoding.Unicode.GetBytes(name);
                uint hash = 0xffffffff, first = 0xcc9e2d51, second = 0x1b873593;
                int blocks = bytes.Length / 4;
                for (int n = 0; n < blocks; n++)
                {
                    uint k = BitConverter.ToUInt32(bytes, n * 4);
                    k *= first; k = (k << 15) | (k >> 17); k *= second;
                    hash ^= k; hash = (hash << 13) | (hash >> 19); hash = hash * 5 + 0xe6546b64;
                }
                uint tail = 0;
                int at = blocks * 4, count = bytes.Length & 3;
                if (count >= 3) tail ^= (uint)bytes[at + 2] << 16;
                if (count >= 2) tail ^= (uint)bytes[at + 1] << 8;
                if (count >= 1) tail ^= bytes[at];
                if (count > 0) { tail *= first; tail = (tail << 15) | (tail >> 17); tail *= second; hash ^= tail; }
                hash ^= (uint)bytes.Length;
                hash ^= hash >> 16; hash *= 0x85ebca6b; hash ^= hash >> 13; hash *= 0xc2b2ae35; hash ^= hash >> 16;
                return hash;
            }
        }
        public static ulong AssetKey(string path)
        {
            return MurmurUtf16(path.ToLowerInvariant()) | ((ulong)MurmurUtf16(path.ToUpperInvariant()) << 32);
        }
    }

    public sealed class ManagerEngine
    {
        public readonly string Home;
        public readonly string Data;
        public readonly string StatePath;
        public Action<string> Log = delegate { };
        // Fault injection is used only by the synthetic install/rollback tests.
        internal Action<string> Fault = delegate { };
        // Synthetic tests run in mock directories and replace only this instance's process guard.
        internal Action ProcessGuard = LiveProcessGuard;
        public ManagerEngine(string home)
        {
            Home = Path.GetFullPath(home);
            Data = Path.Combine(Home, "data");
            StatePath = Path.Combine(Data, "state.json");
        }
        public InstallState ReadState()
        {
            if (!File.Exists(StatePath)) return null;
            var state = Codec.Read<InstallState>(StatePath);
            if (state == null || state.Format != 1) throw new InvalidDataException("管理器状态文件版本不支持。");
            return state;
        }
        public static bool IsActive(InstallState state)
        {
            return state != null && state.Status != "Restored";
        }
        public static string SafeRelative(string value)
        {
            value = value.Replace('\\', '/');
            if (value.StartsWith("/") || value.IndexOf(':') >= 0 || value.IndexOf('\0') >= 0)
                throw new InvalidDataException("文件路径不是安全的相对路径。");
            var parts = value.Split('/');
            if (parts.Any(p => p == ".." || p == "." || p.Length == 0 || p.EndsWith(".") || p.EndsWith(" ")))
                throw new InvalidDataException("文件路径含有不安全的目录段。");
            foreach (string part in parts)
                if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("文件名无效。");
            return value;
        }
        public static string Inside(string root, string relative)
        {
            relative = SafeRelative(relative);
            string absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(Path.Combine(absoluteRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("文件超出指定目录。");
            return destination;
        }
        public static void NoLinks(string root, string destination)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\');
            string current = Path.GetFullPath(destination);
            while (current != null && current.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("目标包含目录链接，不能直接安装：" + current);
                if (String.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
        }
        private static bool AllowedAsset(string relative)
        {
            if (HighHeelAssets.Paths.Contains(relative, StringComparer.OrdinalIgnoreCase) ||
                BreathAssets.Contains(relative)) return true;
            bool prefix = new[] {
                "natives/stm/character/ch/ch01/", "natives/stm/character/ch/ch02/",
                "natives/stm/_ge/character/ch/ch01/", "natives/stm/_ge/character/ch/ch02/",
                "natives/stm/re8mod/",
                "natives/stm/character/ch/prefab/gallery/ch01_0100/",
                "natives/stm/character/ch/prefab/gallery/ch02_1000/",
                "natives/stm/streaming/character/ch/prefab/gallery/ch02_1000/" }.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            return prefix && Regex.IsMatch(relative, @"\.(mesh|mdf2|tex)\.\d+$", RegexOptions.IgnoreCase);
        }
        private static string AssetPath(ZipArchiveEntry entry)
        {
            string full = entry.FullName.Replace('\\', '/');
            if (full.EndsWith("/")) return null;
            full = SafeRelative(full);
            int start = full.StartsWith("natives/", StringComparison.OrdinalIgnoreCase) ? 0 : full.IndexOf("/natives/", StringComparison.OrdinalIgnoreCase) + 1;
            if (start == 0 && !full.StartsWith("natives/", StringComparison.OrdinalIgnoreCase)) return null;
            string path = full.Substring(start).ToLowerInvariant();
            if (!AllowedAsset(path)) throw new InvalidDataException("此工具只接受人物资源及已适配的伊森脚步声、呼吸声资源：" + path);
            return path;
        }
        public List<string> InspectPackage(string archive)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            using (var zip = ZipFile.OpenRead(archive))
                foreach (var entry in zip.Entries)
                {
                    string path = AssetPath(entry);
                    if (path == null) continue;
                    if (!paths.Add(path)) throw new InvalidDataException("压缩包中有重复资源路径：" + path);
                    total = checked(total + entry.Length);
                    if (entry.Length > 512L * 1024 * 1024 || total > 2L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("人物替换包超出大小限制。");
                }
            bool hasAudio = paths.Any(p => HighHeelAssets.Paths.Contains(p, StringComparer.OrdinalIgnoreCase) ||
                BreathAssets.Contains(p));
            bool hasLegacyAudio = HighHeelAssets.LegacyPaths.Any(paths.Contains);
            if (hasLegacyAudio && paths.Contains(HighHeelAssets.NativeBank)) throw new InvalidDataException("不能将旧版独立音效入口与新版原生声音库混装在同一个包内。");
            if (hasLegacyAudio && !HighHeelAssets.LegacyPaths.All(paths.Contains)) throw new InvalidDataException("旧版高跟鞋音效包不完整：需要配套的音效容器、事件表和独立声音库。");
            if (!hasAudio && !paths.Any(p => p.IndexOf(".mesh.", StringComparison.OrdinalIgnoreCase) >= 0)) throw new InvalidDataException("压缩包不含人物模型或已适配的音效资源。");
            return paths.OrderBy(p => p).ToList();
        }
        public void ValidateGame(string game)
        {
            game = Path.GetFullPath(game);
            if (!File.Exists(Path.Combine(game, "re8.exe")) || !File.Exists(Path.Combine(game, "re_chunk_000.pak")))
                throw new DirectoryNotFoundException("请选择包含 re8.exe 和 re_chunk_000.pak 的 RE8 游戏目录。");
            NoLinks(game, game);
        }
        private static void LiveProcessGuard()
        {
            if (Process.GetProcessesByName("re8").Length > 0) throw new IOException("请先退出 RE8，再应用或还原人物替换。");
            if (Process.GetProcessesByName("Modmanager").Length > 0) throw new IOException("请先关闭 Fluffy，避免两个管理器同时修改游戏文件。");
        }
        private void GameClosed() { ProcessGuard(); }
        private static void CheckAudioSelection(HashSet<string> paths)
        {
            if (paths.Contains(HighHeelAssets.NativeBank) && HighHeelAssets.LegacyPaths.Any(paths.Contains))
                throw new InvalidDataException("旧版独立音效包与新版原生音效包不能同时启用，请只保留新版声音包。");
        }
        private List<string> Archives(string game)
        {
            var paths = Directory.GetFiles(game, "*.pak", SearchOption.TopDirectoryOnly).ToList();
            string dlc = Path.Combine(game, "dlc");
            if (Directory.Exists(dlc))
            {
                NoLinks(game, dlc);
                var pending = new Stack<string>(); pending.Push(dlc);
                while (pending.Count > 0)
                {
                    string folder = pending.Pop(); NoLinks(game, folder);
                    paths.AddRange(Directory.GetFiles(folder, "*.pak"));
                    foreach (string next in Directory.GetDirectories(folder)) pending.Push(next);
                }
            }
            return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] result = new byte[count]; int done = 0;
            while (done < count)
            {
                int read = stream.Read(result, done, count - done);
                if (read == 0) throw new EndOfStreamException("游戏归档不完整。");
                done += read;
            }
            return result;
        }
        private static byte[] ReadToc(FileStream stream, out byte[] header)
        {
            header = ReadExactly(stream, 16);
            uint count = BitConverter.ToUInt32(header, 8);
            if (BitConverter.ToUInt32(header, 0) != 1095454795 || header[4] != 4 || header[5] > 1 || BitConverter.ToUInt16(header, 6) != 0)
                throw new InvalidDataException("此版本只支持 RE8 未加密的 v4 游戏归档。");
            if (count > 2000000 || stream.Length < 16L + count * 48L) throw new InvalidDataException("归档目录大小无效。");
            return ReadExactly(stream, checked((int)count * 48));
        }
        private List<PakRecord> Scan(string game, ICollection<string> files, InstallState ownedReadOnly = null)
        {
            var lookup = new Dictionary<ulong, string>();
            foreach (string file in files)
            {
                ulong key = Codec.AssetKey(file);
                if (lookup.ContainsKey(key) && lookup[key] != file) throw new InvalidDataException("资源路径哈希发生冲突。");
                lookup[key] = file;
            }
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<PakRecord>();
            string prefix = game.TrimEnd('\\') + "\\";
            foreach (string pak in Archives(game))
            {
                NoLinks(game, pak);
                using (var stream = File.OpenRead(pak))
                {
                    byte[] header; byte[] toc = ReadToc(stream, out header);
                    string relative = pak.Substring(prefix.Length).Replace('\\', '/');
                    if (ownedReadOnly != null)
                    {
                        var cached = ownedReadOnly.Paks.FirstOrDefault(p => String.Equals(p.Relative, relative, StringComparison.OrdinalIgnoreCase));
                        if (cached == null || stream.Length != cached.Length || Convert.ToBase64String(header) != cached.Header)
                            throw new IOException("当前游戏归档已改变，不能使用旧状态做兼容检查。");
                        foreach (var patch in cached.Patches)
                        {
                            byte[] original = Convert.FromBase64String(patch.Original);
                            if (original.Length != 8 || patch.Index < 0 || patch.Index >= toc.Length / 48) throw new InvalidDataException("已有资源记录无效。");
                            byte[] current = toc.Skip(patch.Index * 48).Take(8).ToArray();
                            if (!current.SequenceEqual(original) && current.Any(v => v != 0)) throw new IOException("当前归档有其他工具的修改。");
                            Buffer.BlockCopy(original, 0, toc, patch.Index * 48, 8);
                        }
                        if (Codec.Digest(toc) != cached.OriginalTocHash) throw new IOException("当前归档有其他修改，兼容检查已停止。");
                    }
                    var record = new PakRecord { Relative = relative, Length = stream.Length,
                        Header = Convert.ToBase64String(header), OriginalTocHash = Codec.Digest(toc) };
                    for (int index = 0; index < toc.Length / 48; index++)
                    {
                        uint lower = BitConverter.ToUInt32(toc, index * 48);
                        if (lower == 0) throw new InvalidDataException("检测到其他管理器启用过的资源。请先在原管理器停用全部 mod，再使用本工具。");
                        ulong key = BitConverter.ToUInt64(toc, index * 48);
                        string matched;
                        if (lookup.TryGetValue(key, out matched))
                        {
                            record.Patches.Add(new HashPatch { Index = index, Original = Convert.ToBase64String(toc.Skip(index * 48).Take(8).ToArray()) });
                            found.Add(matched);
                        }
                    }
                    result.Add(record);
                }
            }
            var missingSlots = files.Where(p => (p.Contains(".mesh.") || String.Equals(p, HighHeelAssets.Container, StringComparison.OrdinalIgnoreCase) || String.Equals(p, HighHeelAssets.NativeBank, StringComparison.OrdinalIgnoreCase) || BreathAssets.Contains(p)) && !found.Contains(p)).ToList();
            if (missingSlots.Count > 0) throw new InvalidDataException("当前游戏归档找不到这些替换槽位：\n" + String.Join("\n", missingSlots.Take(5)));
            return result;
        }
        public object ScanOnly(string game, IList<ModPackage> mods)
        {
            game = Path.GetFullPath(game).TrimEnd('\\'); ValidateGame(game);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods)
                foreach (string path in InspectPackage(mod.Archive))
                    if (!files.Add(path)) throw new InvalidDataException("两个包使用了同一个模型槽位，请只保留一个伊森版本。");
            CheckAudioSelection(files);
            var owned = ReadState();
            if (!IsActive(owned) || !String.Equals(owned.GameRoot, game, StringComparison.OrdinalIgnoreCase)) owned = null;
            if (owned != null)
            {
                string prefix = game.TrimEnd('\\') + "\\";
                var live = Archives(game).Select(p => p.Substring(prefix.Length).Replace('\\', '/'));
                if (!new HashSet<string>(live, StringComparer.OrdinalIgnoreCase).SetEquals(owned.Paks.Select(p => p.Relative)))
                    throw new IOException("游戏归档列表已改变，兼容检查已停止。");
            }
            var paks = Scan(game, files, owned);
            var foundKeys = new HashSet<ulong>(paks.SelectMany(p => p.Patches).Select(p => BitConverter.ToUInt64(Convert.FromBase64String(p.Original), 0)));
            var withoutEntry = files.Where(p => !foundKeys.Contains(Codec.AssetKey(p))).OrderBy(p => p).ToList();
            return new { game = game, mod_count = mods.Count, resources = files.Count, archives = paks.Count,
                matching_archive_entries = paks.Sum(p => p.Patches.Count), assets_without_archive_entry = withoutEntry,
                current_replacements_active = owned != null, read_only = true };
        }
        private void SaveState(InstallState state)
        {
            Codec.Save(StatePath, state);
            Codec.Save(Inside(Data, "sessions/" + state.Session + "/journal.json"), state);
        }
        private static void AtomicCopy(string source, string destination, string boundary)
        {
            NoLinks(boundary, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + ".re8characters.tmp";
            if (File.Exists(temporary) || Directory.Exists(temporary)) throw new IOException("发现未处理的临时文件：" + temporary);
            try
            {
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public InstallState Install(string game, IList<ModPackage> mods)
        {
            GameClosed(); game = Path.GetFullPath(game).TrimEnd('\\'); ValidateGame(game);
            if (mods.Count == 0) throw new InvalidOperationException("请至少选择一个人物或音效包；关闭全部替换请点“停用并还原”。");
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods)
                foreach (string relative in InspectPackage(mod.Archive))
                    if (!unique.Add(relative)) throw new InvalidDataException("人物包有资源冲突，请只保留一个伊森版本。");
            CheckAudioSelection(unique);
            var previous = ReadState();
            if (IsActive(previous))
            {
                if (!String.Equals(previous.GameRoot, game, StringComparison.OrdinalIgnoreCase)) throw new IOException("请先还原旧游戏目录里的替换，再更换目录。");
                // Verify new native slots against an in-memory view of the
                // original TOC before removing the working selection.
                ScanOnly(game, mods);
                Restore();
            }
            string session = Guid.NewGuid().ToString("N");
            var state = new InstallState { GameRoot = game, Session = session, Status = "Preparing",
                CreatedUtc = DateTime.UtcNow.ToString("o"), Mods = mods.ToList() };
            Log("校验并准备人物资源…");
            int ordinal = 0;
            foreach (var mod in mods)
                using (var zip = ZipFile.OpenRead(mod.Archive))
                    foreach (var entry in zip.Entries)
                    {
                        string relative = AssetPath(entry); if (relative == null) continue;
                        var record = new FileRecord { Relative = relative, Stage = "sessions/" + session + "/staging/" + ordinal + ".bin",
                            Backup = "sessions/" + session + "/backups/" + ordinal + ".bin", Length = entry.Length };
                        string stage = Inside(Data, record.Stage);
                        Directory.CreateDirectory(Path.GetDirectoryName(stage));
                        using (var input = entry.Open())
                        using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                        if (new FileInfo(stage).Length != entry.Length) throw new InvalidDataException("压缩包文件长度不正确。");
                        record.InstalledHash = Codec.FileHash(stage);
                        string destination = Inside(game, relative); NoLinks(game, destination);
                        record.OriginalExists = File.Exists(destination);
                        if (record.OriginalExists)
                        {
                            record.OriginalHash = Codec.FileHash(destination);
                            string backup = Inside(Data, record.Backup); Directory.CreateDirectory(Path.GetDirectoryName(backup));
                            File.Copy(destination, backup, false);
                            if (Codec.FileHash(backup) != record.OriginalHash) throw new IOException("原文件备份校验失败。");
                        }
                        state.Files.Add(record); ordinal++;
                    }
            Log("核对游戏资源和冲突…");
            state.Paks = Scan(game, unique);
            GameClosed();
            state.Status = "Installing"; SaveState(state);
            try
            {
                foreach (var record in state.Files)
                {
                    string destination = Inside(game, record.Relative);
                    bool exists = File.Exists(destination);
                    if (exists != record.OriginalExists || (exists && Codec.FileHash(destination) != record.OriginalHash))
                        throw new IOException("安装期间原文件发生变化，请关闭其他 mod 工具。");
                    AtomicCopy(Inside(Data, record.Stage), destination, game);
                    Fault("after_file");
                }
                foreach (var pak in state.Paks.Where(p => p.Patches.Count > 0))
                {
                    GameClosed();
                    string path = Inside(game, pak.Relative);
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        byte[] header; byte[] toc = ReadToc(stream, out header);
                        if (stream.Length != pak.Length || Convert.ToBase64String(header) != pak.Header || Codec.Digest(toc) != pak.OriginalTocHash)
                            throw new IOException("游戏归档在安装期间发生变化。");
                        foreach (var patch in pak.Patches)
                        { stream.Position = 16L + patch.Index * 48L; stream.Write(new byte[8], 0, 8); }
                        stream.Flush(true);
                    }
                    Fault("after_pak");
                }
                state.Status = "Active"; SaveState(state);
                Log("人物替换已应用。可以启动游戏测试。");
                return state;
            }
            catch (Exception installationError)
            {
                try { Restore(); Log("安装未完成，已经回滚至原来的文件。"); }
                catch (Exception recoveryError)
                { throw new IOException("安装中断，自动还原未完成。保留 data 文件夹并点击还原。\n" + installationError.Message + "\n" + recoveryError.Message); }
                throw new IOException("安装未完成，已自动回滚。\n" + installationError.Message, installationError);
            }
        }
        public void Restore()
        {
            GameClosed(); var state = ReadState(); if (!IsActive(state)) { Log("当前没有本工具启用的人物替换。"); return; }
            ValidateGame(state.GameRoot);
            if (!Regex.IsMatch(state.Session ?? "", "^[0-9a-f]{32}$")) throw new InvalidDataException("备份会话无效。");
            var archiveNames = Archives(state.GameRoot).Select(p => p.Substring(state.GameRoot.TrimEnd('\\').Length + 1).Replace('\\', '/')).ToList();
            if (!new HashSet<string>(archiveNames, StringComparer.OrdinalIgnoreCase).SetEquals(state.Paks.Select(p => p.Relative)))
                throw new IOException("游戏归档列表已变化，可能更新了游戏或 DLC；未写入旧备份。");
            Log("核对备份与当前游戏文件…");
            // Preflight every archive and loose file before restoring any bytes.
            foreach (var pak in state.Paks)
            {
                string path = Inside(state.GameRoot, pak.Relative); NoLinks(state.GameRoot, path);
                using (var stream = File.OpenRead(path))
                {
                    byte[] header; byte[] toc = ReadToc(stream, out header);
                    if (stream.Length != pak.Length || Convert.ToBase64String(header) != pak.Header) throw new IOException("游戏归档已更新，不能套用旧备份。");
                    foreach (var patch in pak.Patches)
                    {
                        byte[] original = Convert.FromBase64String(patch.Original);
                        if (original.Length != 8 || patch.Index < 0 || patch.Index >= toc.Length / 48) throw new InvalidDataException("归档备份记录无效。");
                        byte[] current = toc.Skip(patch.Index * 48).Take(8).ToArray();
                        if (!current.SequenceEqual(original) && current.Any(v => v != 0)) throw new IOException("归档已由其他工具修改；还原已停止。");
                        Buffer.BlockCopy(original, 0, toc, patch.Index * 48, 8);
                    }
                    if (Codec.Digest(toc) != pak.OriginalTocHash) throw new IOException("游戏归档已发生其他修改；还原已停止。");
                }
            }
            foreach (var file in state.Files)
            {
                if (!AllowedAsset(file.Relative)) throw new InvalidDataException("备份包含非人物文件。");
                string destination = Inside(state.GameRoot, file.Relative); NoLinks(state.GameRoot, destination);
                if (File.Exists(destination))
                {
                    string current = Codec.FileHash(destination);
                    if (current != file.InstalledHash && (!file.OriginalExists || current != file.OriginalHash))
                        throw new IOException("该文件已被其他工具修改，未覆盖它：" + file.Relative);
                }
                if (file.OriginalExists)
                {
                    string backup = Inside(Data, file.Backup);
                    if (!File.Exists(backup) || Codec.FileHash(backup) != file.OriginalHash) throw new IOException("原文件备份丢失或损坏。");
                }
            }
            GameClosed(); state.Status = "Restoring"; SaveState(state);
            // Restore loose resources before making the original PAK entries visible.
            foreach (var file in state.Files)
            {
                string destination = Inside(state.GameRoot, file.Relative);
                if (file.OriginalExists) AtomicCopy(Inside(Data, file.Backup), destination, state.GameRoot);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            foreach (var pak in state.Paks.Where(p => p.Patches.Count > 0))
                using (var stream = new FileStream(Inside(state.GameRoot, pak.Relative), FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    foreach (var patch in pak.Patches)
                    { byte[] original = Convert.FromBase64String(patch.Original); stream.Position = 16L + patch.Index * 48L; stream.Write(original, 0, 8); }
                    stream.Flush(true);
                }
            state.Status = "Restored"; SaveState(state);
            // Staging files are no longer needed after a completed restoration.
            // Delete only recorded flat files inside this session, never recursively.
            try
            {
                string staging = Inside(Data, "sessions/" + state.Session + "/staging");
                string boundary = staging.TrimEnd('\\') + "\\";
                foreach (var file in state.Files)
                {
                    string staged = Inside(Data, file.Stage);
                    if (!staged.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(Path.GetFileName(staged), "^[0-9]+\\.bin$"))
                        throw new IOException("缓存路径无效，已保留缓存。");
                    NoLinks(Data, staged);
                    if (File.Exists(staged)) File.Delete(staged);
                }
                if (Directory.Exists(staging) && !Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging, false);
            }
            catch (IOException) { Log("游戏文件已还原；部分缓存暂时保留。"); }
            Log("已还原本工具修改的文件；其他文件保留。");
        }
    }
}
