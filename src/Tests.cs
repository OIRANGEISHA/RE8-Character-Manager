using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RE8Characters
{
    internal static class Tests
    {
        private const string Body = "natives/stm/character/ch/ch01/0000/0001/ch01_0001_upperbody.mesh.2101050001";
        private const string Lady = "natives/stm/_ge/character/ch/ch01/7000/7000/ch01_7000_body.mesh.2101050001";
        private const string Texture = "natives/stm/re8mod/selftest/body.tex.30";
        private const string Unmanaged = "natives/stm/character/ch/ch01/0000/0020/keep.mesh.2101050001";
        private const string StoryBody = "natives/stm/character/ch/ch02/1000/1000/ch02_1000_body.mesh.2101050001";
        private const string StoryGallery = "natives/stm/character/ch/prefab/gallery/ch02_1000/ch02_1000.mesh.2101050001";
        private const string StoryStreaming = "natives/stm/streaming/character/ch/prefab/gallery/ch02_1000/ch02_1020_facegallery_albd.tex.30";
        private const string EthanGallery = "natives/stm/character/ch/prefab/gallery/ch01_0100/ch01_0100_body.mesh.2101050001";
        private const string SharedFootBank = "natives/stm/sound/wwise/pl1000_mt_foot.bnk.2.x64";
        private static List<string> passed = new List<string>();
        private static string testRoot;
        private sealed class Fixture
        {
            public string Root, Game, BasePak, PatchPak, BodyFile, StoryBodyFile, StoryStreamingFile;
            public ManagerEngine Engine;
            public ModPackage Ethan, Mrs, Duplicate, OriginalLady, HighHeels, LegacyHighHeels, Breaths;
            public byte[] OriginalBase, OriginalPatch;
        }
        private static void Assert(bool value, string message)
        { if (!value) throw new Exception("TEST FAILED: " + message); }
        private static void Rejected(Action action, string message)
        {
            bool failed = false;
            try { action(); } catch (Exception) { failed = true; }
            Assert(failed, message);
        }
        private static void Test(string name, Action action)
        { action(); passed.Add(name); }
        private static void WritePak(string path, params string[] assets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var names = assets.OrderBy(Codec.AssetKey).ToArray();
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((uint)1095454795); writer.Write((byte)4); writer.Write((byte)0); writer.Write((ushort)0);
                writer.Write((uint)names.Length); writer.Write((uint)0x10203040);
                long offset = 16 + names.Length * 48;
                for (int index = 0; index < names.Length; index++)
                {
                    writer.Write(Codec.MurmurUtf16(names[index].ToLowerInvariant()));
                    writer.Write(Codec.MurmurUtf16(names[index].ToUpperInvariant()));
                    writer.Write((ulong)(offset + index * 4)); writer.Write((ulong)4); writer.Write((ulong)4);
                    writer.Write((ulong)0); writer.Write((ulong)(123 + index));
                }
                for (int index = 0; index < names.Length; index++) writer.Write(0x12345678 + index);
            }
        }
        private static void WriteZip(string path, Dictionary<string, string> contents)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var pair in contents)
                    using (var writer = new StreamWriter(zip.CreateEntry(pair.Key).Open(), new UTF8Encoding(false))) writer.Write(pair.Value);
        }
        private static Fixture Make(string name)
        {
            string root = Path.Combine(testRoot, name), game = Path.Combine(root, "mock-game"), app = Path.Combine(root, "manager");
            Directory.CreateDirectory(game); Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(game, "re8.exe"), "synthetic test marker - not an executable");
            string basePak = Path.Combine(game, "re_chunk_000.pak"), patchPak = Path.Combine(game, "dlc", "re_dlc_test.pak");
            WritePak(basePak, Body, Lady, Unmanaged, StoryBody, StoryGallery, StoryStreaming, EthanGallery, HighHeelAssets.Container, SharedFootBank, BreathAssets.EnglishBank); WritePak(patchPak, Body, Lady);
            string body = ManagerEngine.Inside(game, Body); Directory.CreateDirectory(Path.GetDirectoryName(body)); File.WriteAllText(body, "original loose body");
            string storyBody = ManagerEngine.Inside(game, StoryBody), storyStreaming = ManagerEngine.Inside(game, StoryStreaming);
            Directory.CreateDirectory(Path.GetDirectoryName(storyBody)); File.WriteAllText(storyBody, "original NPC body");
            Directory.CreateDirectory(Path.GetDirectoryName(storyStreaming)); File.WriteAllText(storyStreaming, "original streaming texture");
            string container = ManagerEngine.Inside(game, HighHeelAssets.Container), sharedFoot = ManagerEngine.Inside(game, SharedFootBank);
            Directory.CreateDirectory(Path.GetDirectoryName(container)); File.WriteAllText(container, "original Ethan audio container");
            Directory.CreateDirectory(Path.GetDirectoryName(sharedFoot)); File.WriteAllText(sharedFoot, "shared bank must remain unchanged");
            File.WriteAllText(ManagerEngine.Inside(game, BreathAssets.EnglishBank), "original English voice bank");
            string ethan = Path.Combine(root, "ethan.zip"), lady = Path.Combine(root, "lady.zip"), duplicate = Path.Combine(root, "duplicate.zip"), original = Path.Combine(root, "original_lady.zip");
            WriteZip(ethan, new Dictionary<string, string> { { Body, "replacement Ethan" }, { Texture, "private texture" }, { "README.txt", "metadata only" } });
            WriteZip(lady, new Dictionary<string, string> { { Lady, "replacement Lady" } });
            WriteZip(duplicate, new Dictionary<string, string> { { Body, "conflicting body" } });
            WriteZip(original, new Dictionary<string, string> { { StoryBody, "replacement NPC body" }, { StoryGallery, "replacement gallery model" }, { StoryStreaming, "replacement streaming texture" } });
            string audio = Path.Combine(root, "high_heels.zip");
            WriteZip(audio, new Dictionary<string,string> { { HighHeelAssets.NativeBank, "native Lady walking bank" } });
            string legacyAudio = Path.Combine(root, "high_heels_legacy.zip");
            WriteZip(legacyAudio, new Dictionary<string,string> { { HighHeelAssets.Container, "private audio container" }, { HighHeelAssets.Events, "private event table" }, { HighHeelAssets.Bank, "private Lady walking bank" } });
            string breaths = Path.Combine(root, "breaths.zip");
            WriteZip(breaths, new Dictionary<string,string> { { BreathAssets.EnglishBank, "Lady breathing in English Ethan bank" } });
            var engine = new ManagerEngine(app);
            engine.ProcessGuard = delegate { }; // Only this mock directory is used by this instance.
            return new Fixture { Root = root, Game = game, BasePak = basePak, PatchPak = patchPak, BodyFile = body,
                StoryBodyFile = storyBody, StoryStreamingFile = storyStreaming,
                OriginalBase = File.ReadAllBytes(basePak), OriginalPatch = File.ReadAllBytes(patchPak), Engine = engine,
                Ethan = new ModPackage { Id = "story_ethan", Name = "Story Ethan", Group = "ethan", Archive = ethan },
                Mrs = new ModPackage { Id = "mercenary_lady", Name = "Mercenary Lady", Group = "lady", Archive = lady },
                Duplicate = new ModPackage { Id = "mercenary_ethan", Name = "Mercenary Ethan", Group = "ethan", Archive = duplicate },
                OriginalLady = new ModPackage { Id = "story_lady", Name = "Original Story Lady", Group = "story_lady", Archive = original },
                HighHeels = new ModPackage { Id = "ethan_high_heels", Name = "High heels v0.2", Group = "audio", Archive = audio },
                LegacyHighHeels = new ModPackage { Id = "ethan_high_heels", Name = "High heels v0.1", Group = "audio", Archive = legacyAudio },
                Breaths = new ModPackage { Id = "ethan_breath_en", Name = "English Lady breathing", Group = "breath_audio", Archive = breaths } };
        }
        private static void Original(Fixture fixture)
        {
            Assert(File.ReadAllBytes(fixture.BasePak).SequenceEqual(fixture.OriginalBase), "base PAK is not byte-identical after restore");
            Assert(File.ReadAllBytes(fixture.PatchPak).SequenceEqual(fixture.OriginalPatch), "DLC PAK is not byte-identical after restore");
            Assert(File.ReadAllText(fixture.BodyFile) == "original loose body", "preexisting loose file not restored");
            Assert(!File.Exists(ManagerEngine.Inside(fixture.Game, Texture)), "new private texture not removed");
            Assert(!File.Exists(ManagerEngine.Inside(fixture.Game, Lady)), "new Lady mesh not removed");
            Assert(File.ReadAllText(fixture.StoryBodyFile) == "original NPC body", "NPC body original not preserved");
            Assert(File.ReadAllText(fixture.StoryStreamingFile) == "original streaming texture", "streaming original not preserved");
            Assert(!File.Exists(ManagerEngine.Inside(fixture.Game, StoryGallery)), "new gallery model not removed");
            Assert(!File.Exists(ManagerEngine.Inside(fixture.Game, EthanGallery)), "Ethan gallery model not removed");
            Assert(File.ReadAllText(ManagerEngine.Inside(fixture.Game, HighHeelAssets.Container)) == "original Ethan audio container", "audio container not restored");
            Assert(File.ReadAllText(ManagerEngine.Inside(fixture.Game, SharedFootBank)) == "shared bank must remain unchanged", "shared bank was modified");
            Assert(File.ReadAllText(ManagerEngine.Inside(fixture.Game, BreathAssets.EnglishBank)) == "original English voice bank", "English voice original not preserved");
            Assert(BreathAssets.Paths.Where(p => p != BreathAssets.EnglishBank).All(p => !File.Exists(ManagerEngine.Inside(fixture.Game, p))), "new localized breathing banks not removed");
            Assert(!File.Exists(ManagerEngine.Inside(fixture.Game, HighHeelAssets.Events)) && !File.Exists(ManagerEngine.Inside(fixture.Game, HighHeelAssets.Bank)), "private audio resources not removed");
            Assert(!ManagerEngine.IsActive(fixture.Engine.ReadState()), "state still active after restore");
        }
        public static void Run(string home)
        {
            testRoot = Path.Combine(home, "tests", "run_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testRoot);
            Test("UTF16 Murmur hashes match the existing RE8 asset parser", delegate {
                Assert(Codec.MurmurUtf16(Body.ToLowerInvariant()) == 3591133729U, "lower hash differs from RE8 parser");
                Assert(Codec.MurmurUtf16(Body.ToUpperInvariant()) == 1225357214U, "upper hash differs from RE8 parser");
                Assert(Codec.MurmurUtf16(Lady.ToLowerInvariant()) == 2882990052U, "DLC lower hash differs");
            });
            Test("Install and exact restore across base and DLC archives", delegate {
                var f = Make("roundtrip");
                f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs });
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "body did not install");
                Assert(File.Exists(ManagerEngine.Inside(f.Game, Lady)), "Lady did not install");
                Assert(!File.ReadAllBytes(f.BasePak).SequenceEqual(f.OriginalBase), "PAK entries were not invalidated");
                f.Engine.Restore(); Original(f);
            });
            Test("One-click keeps all character targets and removes known-bad audio", delegate {
                var f = Make("all"); var all = Catalog.AllTargets(new List<ModPackage> { f.Ethan, f.Duplicate, f.Mrs, f.OriginalLady, f.HighHeels });
                Assert(all.Count == 3 && !all.Any(p => p.Id == "ethan_high_heels") && all.Any(p => p.Id == "story_ethan") && all.Any(p => p.Id == "story_lady") && !all.Any(p => p.Id == "mercenary_ethan"), "all button excluded characters or enabled known-bad audio/conflicting Ethans");
                f.Engine.Install(f.Game, new[] { f.HighHeels });
                f.Engine.Install(f.Game, all);
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.NativeBank)) == "shared bank must remain unchanged", "one-click did not restore the old sound bank");
                f.Engine.Restore(); Original(f);
            });
            Test("Catalog selects repaired packages and excludes old built-ins from imports", delegate {
                string app = Path.Combine(testRoot, "catalog", "manager"), mods = Path.Combine(app, "mods");
                Directory.CreateDirectory(mods);
                foreach (string filename in Catalog.BuiltInFiles.Concat(Catalog.PreviousBuiltInFiles))
                    File.WriteAllText(Path.Combine(mods, filename), "catalog path fixture");
                var catalog = Catalog.Load(app);
                Assert(catalog.Count == 5 && catalog.All(m => m.Group != "custom") && !catalog.Any(m => m.Id == "ethan_high_heels"), "legacy or paused footstep packages reappeared in catalog");
                Assert(catalog.Single(m => m.Id == "story_ethan").Archive.EndsWith("_v0.4.zip") &&
                    catalog.Single(m => m.Id == "mercenary_lady").Archive.EndsWith("_v0.4.zip") &&
                    catalog.Single(m => m.Id == "mercenary_ethan").Archive.EndsWith("_v0.4.zip") &&
                    catalog.Single(m => m.Id == "ethan_breath_en").Archive.EndsWith("_MultiLang_TEST_v0.2.zip"), "catalog selected old packages");
            });
            Test("Upgrading a package with the same ID preserves original restore ownership", delegate {
                var f = Make("version_upgrade"); f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs, f.OriginalLady });
                string repaired = Path.Combine(f.Root, "ethan_v0.2.zip");
                WriteZip(repaired, new Dictionary<string,string> { { Body, "repaired body and head" }, { Texture, "repaired texture" } });
                var next = new ModPackage { Id = f.Ethan.Id, Name = f.Ethan.Name, Group = f.Ethan.Group, Archive = repaired };
                f.Engine.Install(f.Game, new[] { next, f.Mrs, f.OriginalLady });
                Assert(File.ReadAllText(f.BodyFile) == "repaired body and head", "new version was not applied");
                f.Engine.Restore(); Original(f);
            });
            Test("Original Lady gallery and streaming resources install and restore", delegate {
                var f = Make("original_lady"); f.Engine.Install(f.Game, new[] { f.OriginalLady });
                Assert(File.ReadAllText(f.StoryBodyFile) == "replacement NPC body", "NPC body missing");
                Assert(File.ReadAllText(f.StoryStreamingFile) == "replacement streaming texture", "streaming texture missing");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, StoryGallery)) == "replacement gallery model", "Gallery mesh missing");
                f.Engine.Restore(); Original(f);
            });
            Test("Ethan model-viewer gallery slot installs and restores with its archive entry", delegate {
                var f = Make("ethan_gallery"); string archive = Path.Combine(f.Root, "gallery.zip");
                WriteZip(archive, new Dictionary<string,string> { { EthanGallery, "Lady over gallery Ethan" } });
                f.Engine.Install(f.Game, new[] { new ModPackage { Id = "story_ethan", Name = "Gallery Ethan", Group = "ethan", Archive = archive } });
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, EthanGallery)) == "Lady over gallery Ethan", "gallery model missing");
                f.Engine.Restore(); Original(f);
            });
            Test("Active read-only compatibility scan accepts trailing slash without modifying game", delegate {
                var f = Make("active_scan"); f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs });
                byte[] currentPak = File.ReadAllBytes(f.BasePak), currentDlc = File.ReadAllBytes(f.PatchPak);
                string stateHash = Codec.FileHash(f.Engine.StatePath), looseHash = Codec.FileHash(f.BodyFile);
                f.Engine.ScanOnly(f.Game + "\\", new[] { f.Ethan, f.Mrs, f.OriginalLady });
                Assert(File.ReadAllBytes(f.BasePak).SequenceEqual(currentPak) && File.ReadAllBytes(f.PatchPak).SequenceEqual(currentDlc), "read-only scan wrote PAK bytes");
                Assert(Codec.FileHash(f.Engine.StatePath) == stateHash && Codec.FileHash(f.BodyFile) == looseHash, "read-only scan changed install state or files");
                f.Engine.Restore(); Original(f);
            });
            Test("Running-game process guard blocks mutations", delegate {
                var f = Make("process_guard"); f.Engine.ProcessGuard = delegate { throw new IOException("synthetic game is running"); };
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.OriginalLady }); }, "process guard did not stop install"); Original(f);
            });
            Test("Conflicting slots fail before game changes", delegate {
                var f = Make("conflict"); Rejected(delegate { f.Engine.Install(f.Game, new[] { f.Ethan, f.Duplicate }); }, "conflicting packages accepted"); Original(f);
            });
            Test("Switching packages restores old assets first", delegate {
                var f = Make("switch"); f.Engine.Install(f.Game, new[] { f.Ethan }); f.Engine.Install(f.Game, new[] { f.Mrs });
                Assert(File.ReadAllText(f.BodyFile) == "original loose body", "old Ethan remained after switching");
                Assert(!File.Exists(ManagerEngine.Inside(f.Game, Texture)), "old private texture remained");
                f.Engine.Restore(); Original(f);
            });
            Test("Failure after copying a resource rolls back all changes", delegate {
                var f = Make("file_failure"); f.Engine.Fault = delegate(string point) { if (point == "after_file") throw new IOException("injected disk failure"); };
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs }); }, "failure was not reported"); Original(f);
            });
            Test("Failure after modifying PAK hashes restores archive bytes", delegate {
                var f = Make("pak_failure"); f.Engine.Fault = delegate(string point) { if (point == "after_pak") throw new IOException("injected archive failure"); };
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs }); }, "PAK failure was not reported"); Original(f);
            });
            Test("Interrupted journal can be recovered after restarting", delegate {
                var f = Make("recover"); var state = f.Engine.Install(f.Game, new[] { f.Ethan }); state.Status = "Installing"; Codec.Save(f.Engine.StatePath, state);
                var restarted = new ManagerEngine(f.Engine.Home); restarted.ProcessGuard = delegate { }; restarted.Restore(); Original(f);
            });
            Test("Changed loose files are preserved and restore refuses", delegate {
                var f = Make("foreign_file"); f.Engine.Install(f.Game, new[] { f.Ethan }); File.WriteAllText(f.BodyFile, "external user change");
                Rejected(delegate { f.Engine.Restore(); }, "foreign change overwritten");
                Assert(File.ReadAllText(f.BodyFile) == "external user change", "external content damaged");
                Assert(File.Exists(ManagerEngine.Inside(f.Game, Texture)), "preflight already deleted another file");
                var state = f.Engine.ReadState(); File.Copy(ManagerEngine.Inside(f.Engine.Data, state.Files.First(p => p.Relative == Body).Stage), f.BodyFile, true);
                f.Engine.Restore(); Original(f);
            });
            Test("Updated game archive blocks stale restoration", delegate {
                var f = Make("game_update"); f.Engine.Install(f.Game, new[] { f.Ethan });
                using (var stream = new FileStream(f.BasePak, FileMode.Open, FileAccess.Write)) { stream.Position = 12; stream.WriteByte(255); }
                Rejected(delegate { f.Engine.Restore(); }, "updated game archive overwritten");
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "update preflight changed loose file");
                using (var stream = new FileStream(f.BasePak, FileMode.Open, FileAccess.Write)) { stream.Position = 12; stream.Write(f.OriginalBase, 12, 4); }
                f.Engine.Restore(); Original(f);
            });
            Test("Existing invalidation by another manager is rejected", delegate {
                var f = Make("other_manager");
                using (var stream = new FileStream(f.BasePak, FileMode.Open, FileAccess.Write)) { stream.Position = 16; stream.Write(new byte[8], 0, 8); }
                byte[] before = File.ReadAllBytes(f.BasePak);
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.Ethan }); }, "existing mod manager changes accepted");
                Assert(File.ReadAllBytes(f.BasePak).SequenceEqual(before), "other manager's archive changed");
                Assert(File.ReadAllText(f.BodyFile) == "original loose body", "original loose file changed before preflight");
            });
            Test("ZIP traversal, duplicate paths and non-character files rejected", delegate {
                var f = Make("bad_zip");
                foreach (var pair in new[] { "../natives/stm/re8mod/body.mesh.2101050001", "natives/stm/character/ch/ch01/dinput8.dll", "natives/stm/character/ch/ch01/../outside.mesh.2101050001", "natives/stm/character/ch/prefab/gallery/ch01_0000/body.mesh.2101050001" }) {
                    string archive = Path.Combine(f.Root, Guid.NewGuid().ToString("N") + ".zip"); WriteZip(archive, new Dictionary<string, string> { { pair, "unsafe" } });
                    Rejected(delegate { f.Engine.InspectPackage(archive); }, "unsafe ZIP accepted");
                }
                string duplicate = Path.Combine(f.Root, "case_duplicate.zip"); WriteZip(duplicate, new Dictionary<string, string> { { Body, "one" }, { Body.ToUpperInvariant(), "two" } });
                Rejected(delegate { f.Engine.InspectPackage(duplicate); }, "case duplicate ZIP accepted");
            });
            Test("Missing original backup stops before changing game", delegate {
                var f = Make("backup_missing"); var state = f.Engine.Install(f.Game, new[] { f.Ethan });
                var record = state.Files.First(p => p.OriginalExists); string backup = ManagerEngine.Inside(f.Engine.Data, record.Backup); byte[] bytes = File.ReadAllBytes(backup); File.Delete(backup);
                Rejected(delegate { f.Engine.Restore(); }, "missing backup was ignored");
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "missing backup preflight changed body");
                File.WriteAllBytes(backup, bytes); f.Engine.Restore(); Original(f);
            });
            Test("Native audio-only install patches one bank and restores exact original bytes", delegate {
                var f = Make("audio_only"); var state = f.Engine.Install(f.Game, new[] { f.HighHeels });
                Assert(state.Files.Count == 1 && state.Paks.Sum(p => p.Patches.Count) == 1, "audio changed more than the native bank and its archive entry");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.NativeBank)) == "native Lady walking bank", "native sound bank missing");
                Assert(File.ReadAllText(f.BodyFile) == "original loose body", "audio-only install changed the model");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.Container)) == "original Ethan audio container", "native sound pack changed event dispatch");
                f.Engine.Restore(); Original(f);
            });
            Test("Audio can be switched off while keeping active model replacements", delegate {
                var f = Make("audio_toggle"); f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs, f.HighHeels });
                f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs });
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "model lost when disabling audio");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.Container)) == "original Ethan audio container", "audio container was not restored");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, SharedFootBank)) == "shared bank must remain unchanged", "native bank was not restored when disabling audio");
                Assert(!File.Exists(ManagerEngine.Inside(f.Game, HighHeelAssets.Bank)) && !File.Exists(ManagerEngine.Inside(f.Game, HighHeelAssets.Events)), "private audio remained after disabling it");
                f.Engine.Restore(); Original(f);
            });
            Test("Incomplete audio and unrelated shared sound replacements are rejected before active changes", delegate {
                var f = Make("audio_bad_package"); f.Engine.Install(f.Game, new[] { f.Ethan });
                foreach (string resource in new[] { HighHeelAssets.Bank, "natives/stm/sound/wwise/pl1000_se.bnk.2.x64" }) {
                    string archive = Path.Combine(f.Root, Guid.NewGuid().ToString("N") + ".zip");
                    WriteZip(archive, new Dictionary<string,string> { { resource, "partial or unrelated audio" } });
                    Rejected(delegate { f.Engine.Install(f.Game, new[] { new ModPackage { Id = "audio", Archive = archive } }); }, "unsafe audio package accepted");
                    Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan" && f.Engine.ReadState().Status == "Active", "invalid audio package disturbed active models");
                }
                f.Engine.Restore(); Original(f);
            });
            Test("Audio installation failure restores original container and removes private files", delegate {
                var f = Make("audio_rollback"); int copies = 0;
                f.Engine.Fault = delegate(string point) { if (point == "after_file" && ++copies == 3) throw new IOException("injected audio copy failure"); };
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.LegacyHighHeels }); }, "audio failure was not reported"); Original(f);
            });
            Test("Audio refuses unsupported game archives without the Ethan container", delegate {
                var f = Make("audio_missing_container"); WritePak(f.BasePak, Body, Lady, SharedFootBank); f.OriginalBase = File.ReadAllBytes(f.BasePak);
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.LegacyHighHeels }); }, "audio missing-container check did not run"); Original(f);
            });
            Test("Private-to-native sound upgrade restores the old dispatch and removes obsolete files", delegate {
                var f = Make("audio_upgrade"); f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs, f.LegacyHighHeels });
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.Container)) == "private audio container", "legacy fixture not applied");
                f.Engine.Install(f.Game, new[] { f.Ethan, f.Mrs, f.HighHeels });
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "upgrade lost model");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.Container)) == "original Ethan audio container", "old dispatch not restored");
                Assert(!File.Exists(ManagerEngine.Inside(f.Game, HighHeelAssets.Events)) && !File.Exists(ManagerEngine.Inside(f.Game, HighHeelAssets.Bank)), "old private audio remains");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.NativeBank)) == "native Lady walking bank", "new audio not installed");
                f.Engine.Restore(); Original(f);
            });
            Test("Mixing private and native dispatch in a package is rejected before changing active files", delegate {
                var f = Make("audio_mixed"); f.Engine.Install(f.Game, new[] { f.LegacyHighHeels });
                var contents = HighHeelAssets.Paths.ToDictionary(p => p, p => "mixed audio");
                string archive = Path.Combine(f.Root, "mixed.zip"); WriteZip(archive, contents);
                Rejected(delegate { f.Engine.Install(f.Game, new[] { new ModPackage { Id = "mixed", Archive = archive } }); }, "mixed dispatch accepted");
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.LegacyHighHeels, f.HighHeels }); }, "two incompatible audio packages accepted");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, HighHeelAssets.Container)) == "private audio container", "bad upgrade changed active files");
                f.Engine.Restore(); Original(f);
            });
            Test("Native sound requires its existing archive entry", delegate {
                var f = Make("audio_missing_bank"); WritePak(f.BasePak, Body, Lady, HighHeelAssets.Container); f.OriginalBase = File.ReadAllBytes(f.BasePak);
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.HighHeels }); }, "missing native bank entry ignored"); Original(f);
            });
            Test("Native bank copy failure restores the preexisting bank and archive bytes", delegate {
                var f = Make("native_audio_rollback");
                f.Engine.Fault = delegate(string point) { if (point == "after_file") throw new IOException("injected native audio failure"); };
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.HighHeels }); }, "native audio failure was not reported"); Original(f);
            });
            Test("Breathing is opt-in for one-click and never enables footsteps", delegate {
                var f = Make("breath_selection");
                var packages = new List<ModPackage> { f.Ethan, f.Mrs, f.OriginalLady, f.HighHeels, f.Breaths };
                Assert(Catalog.AllTargets(packages).Count == 3, "breathing enabled without being checked");
                var selected = Catalog.AllTargets(packages, true);
                Assert(selected.Count == 4 && selected.Contains(f.Breaths) && !selected.Contains(f.HighHeels), "optional selection changed footstep policy");
                f.Engine.Install(f.Game, selected);
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, BreathAssets.EnglishBank)) == "Lady breathing in English Ethan bank", "breathing was not installed");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, SharedFootBank)) == "shared bank must remain unchanged", "breathing changed footsteps");
                f.Engine.Install(f.Game, Catalog.AllTargets(packages));
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "disabling breathing removed character selection");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, BreathAssets.EnglishBank)) == "original English voice bank", "breathing original not restored");
                f.Engine.Restore(); Original(f);
            });
            Test("Breathing bank without an archive slot fails before changing active characters", delegate {
                var f = Make("breath_missing_slot");
                WritePak(f.BasePak, Body, Lady, HighHeelAssets.Container, SharedFootBank); f.OriginalBase = File.ReadAllBytes(f.BasePak);
                f.Engine.Install(f.Game, new[] { f.Ethan });
                byte[] activePak = File.ReadAllBytes(f.BasePak);
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.Ethan, f.Breaths }); }, "missing voice slot was accepted");
                Assert(File.ReadAllBytes(f.BasePak).SequenceEqual(activePak) && File.ReadAllText(f.BodyFile) == "replacement Ethan", "rejected voice install modified active characters");
                f.Engine.Restore(); Original(f);
            });
            Test("Breathing support rejects unadapted languages and other character voice banks", delegate {
                var f = Make("breath_scope");
                foreach (string resource in new[] { BreathAssets.EnglishBank.Replace(".en", ".ko"), BreathAssets.EnglishBank.Replace(".en", ".zhcn.extra"), "natives/stm/sound/wwise/pl6000_v_ge.bnk.2.x64.en", "natives/stm/sound/wwise/em1000_v.bnk.2.x64.en" }) {
                    string archive = Path.Combine(f.Root, Path.GetFileName(resource) + ".zip");
                    WriteZip(archive, new Dictionary<string,string> { { resource, "unadapted voice bank" } });
                    Rejected(delegate { f.Engine.InspectPackage(archive); }, "unadapted voice resource accepted");
                }
                Original(f);
            });
            Test("English to nine-language upgrade preserves choices and restores all voice banks", delegate {
                var f = Make("breath_multilang_upgrade");
                WritePak(f.BasePak, new[] { Body, Lady, Unmanaged, StoryBody, StoryGallery, StoryStreaming, EthanGallery,
                    HighHeelAssets.Container, SharedFootBank }.Concat(BreathAssets.Paths).ToArray());
                f.OriginalBase = File.ReadAllBytes(f.BasePak);
                var characters = new List<ModPackage> { f.Ethan, f.Mrs, f.OriginalLady };
                f.Engine.Install(f.Game, characters.Concat(new[] { f.Breaths }).ToList());
                string archive = Path.Combine(f.Root, "multilang.zip");
                WriteZip(archive, BreathAssets.Paths.ToDictionary(p => p, p => "Lady breathing " + Path.GetExtension(p)));
                var updated = new ModPackage { Id = f.Breaths.Id, Name = "Nine language breathing", Group = "breath_audio", Archive = archive };
                bool wasChecked = f.Engine.ReadState().Mods.Any(m => m.Id == updated.Id);
                var selected = Catalog.AllTargets(characters.Concat(new[] { updated, f.HighHeels }).ToList(), wasChecked);
                Assert(selected.Count == 4 && selected.Contains(updated) && !selected.Contains(f.HighHeels), "upgrade lost breath selection or enabled footsteps");
                f.Engine.Install(f.Game, selected);
                foreach (string path in BreathAssets.Paths)
                    Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, path)) == "Lady breathing " + Path.GetExtension(path), "localized bank missing");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, SharedFootBank)) == "shared bank must remain unchanged", "multilang upgrade changed footsteps");
                f.Engine.Install(f.Game, characters);
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "disabling localized breathing removed characters");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, BreathAssets.EnglishBank)) == "original English voice bank", "English bank not restored after upgrade");
                Assert(BreathAssets.Paths.Where(p => p != BreathAssets.EnglishBank).All(p => !File.Exists(ManagerEngine.Inside(f.Game, p))), "localized bank remained after disabling");
                f.Engine.Restore(); Original(f);
            });
            Test("Missing localized slot rejects upgrade while keeping active English breathing and models", delegate {
                var f = Make("breath_multilang_missing_slot");
                f.Engine.Install(f.Game, new[] { f.Ethan, f.Breaths });
                byte[] activePak = File.ReadAllBytes(f.BasePak), activeState = File.ReadAllBytes(f.Engine.StatePath);
                string archive = Path.Combine(f.Root, "multilang.zip");
                WriteZip(archive, BreathAssets.Paths.ToDictionary(p => p, p => "localized breathing"));
                var updated = new ModPackage { Id = f.Breaths.Id, Name = "Nine language breathing", Group = "breath_audio", Archive = archive };
                Rejected(delegate { f.Engine.Install(f.Game, new[] { f.Ethan, updated }); }, "missing localized slot accepted");
                Assert(File.ReadAllBytes(f.BasePak).SequenceEqual(activePak) && File.ReadAllBytes(f.Engine.StatePath).SequenceEqual(activeState), "rejected upgrade changed active state");
                Assert(File.ReadAllText(f.BodyFile) == "replacement Ethan", "rejected upgrade removed model");
                Assert(File.ReadAllText(ManagerEngine.Inside(f.Game, BreathAssets.EnglishBank)) == "Lady breathing in English Ethan bank", "rejected upgrade removed tested English breathing");
                f.Engine.Restore(); Original(f);
            });
            Codec.Save(Path.Combine(home, "test_report.json"), new { passed_count = passed.Count, tests = passed,
                test_directory = testRoot, real_game_modified = false });
        }
    }
}
