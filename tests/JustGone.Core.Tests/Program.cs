using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JustGone.Core;

namespace JustGone.Core.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("BoardCoord_BoundsAndKeys", TestBoardCoordBoundsAndKeys);
            Run("BoardCoord_GtpRoundTrip_9_13_19", TestBoardCoordGtpRoundTrip);
            Run("SceneNames_ClassificationAndMapping", TestSceneNames);
            Run("MenuRouting_VanillaAndWeiXiangQiCoexistence", TestMenuRoutingCoexistence);
            Run("MenuRouting_MultiModRegistrationAndUnregister", TestMenuRoutingMultiMod);
            Run("SessionGuard_AllTransitions", TestSessionGuard);
            Run("SaveTransactionRules_CasAndReconcile", TestSaveTransactionRules);
            Run("AtomicFileWriter_CreateReplaceBackupAndFailureCleanup", TestAtomicFileWriter);
            Run("MiniJson_RoundTripAndValidation", TestMiniJson);
            Run("PieceLedger_DeterministicDeltaPlanAndVacatedCellMove", TestPieceLedger);
            Run("DisposableTracker_LifoAndExceptionIsolation", TestDisposableTracker);
            Run("InputLockRegistry_MultiModRefCountIsolation", TestInputLockRegistry);

            Console.WriteLine();
            Console.WriteLine("JustGone.Core.Tests: passed=" + _passed + ", failed=" + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + " (expected=" + expected + ", actual=" + actual + ")");
        }

        private static void TestBoardCoordBoundsAndKeys()
        {
            Assert(!BoardCoord.Invalid.IsValid, "Invalid coord must report IsValid=false");
            var c = new BoardCoord(3, 15);
            Assert(c.IsValid, "Valid coord");
            Assert(c.InBounds(19), "(3,15) in 19x19");
            Assert(!c.InBounds(13), "(3,15) out of 13x13");
            int key = c.ToKey(19);
            AssertEqual(c, BoardCoord.FromKey(key, 19), "Key round-trip");
            AssertEqual(BoardCoord.Invalid, BoardCoord.FromKey(-1, 19), "Negative key -> Invalid");
            AssertEqual(BoardCoord.Invalid, BoardCoord.FromKey(361, 19), "Overflow key -> Invalid");
        }

        private static void TestBoardCoordGtpRoundTrip()
        {
            int[] sizes = { 9, 13, 19 };
            for (int s = 0; s < sizes.Length; s++)
            {
                int size = sizes[s];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var coord = new BoardCoord(x, y);
                        string gtp = coord.ToGtp(size);
                        Assert(!gtp.StartsWith("I"), "GTP must skip letter I: " + gtp);
                        Assert(BoardCoord.TryParseGtp(gtp, size, out BoardCoord parsed), "Parse GTP failed: " + gtp);
                        AssertEqual(coord, parsed, "GTP round-trip mismatch for " + gtp);
                    }
                }
            }
            Assert(!BoardCoord.TryParseGtp("I5", 19, out _), "Letter I must be rejected");
            Assert(!BoardCoord.TryParseGtp("Z99", 19, out _), "Out-of-range GTP must be rejected");
        }

        private static void TestSceneNames()
        {
            AssertEqual(SceneKind.MainMenu, JustGoSceneNames.Classify("MainScene"), "MainScene");
            AssertEqual(SceneKind.ChineseGarden, JustGoSceneNames.Classify("GameScene_ChineseGarden"), "ChineseGarden");
            Assert(JustGoSceneNames.IsBoardScene(SceneKind.ChineseGarden), "ChineseGarden is board scene");
            Assert(!JustGoSceneNames.IsBoardScene(SceneKind.MainMenu), "MainMenu is not board scene");
            AssertEqual("GameScene_JapaneseRoom", JustGoSceneNames.ForEnvironment(GameEnvironmentKind.Tatami), "Tatami scene");
            AssertEqual(0, JustGoSceneNames.ToEs3BoardSizeIndex(BoardSizeKind.Nineteen), "19 -> 0");
            AssertEqual(1, JustGoSceneNames.ToEs3BoardSizeIndex(BoardSizeKind.Thirteen), "13 -> 1");
            AssertEqual(2, JustGoSceneNames.ToEs3BoardSizeIndex(BoardSizeKind.Nine), "9 -> 2");
        }

        private static void TestMenuRoutingCoexistence()
        {
            var table = new MenuRouteTable(2);
            var jgMode = table.Register("example.sandbox", "mod.a", "Sandbox Mode");
            Assert(!table.TryAssignIndex("example.sandbox", 0), "Must refuse hijacking vanilla index 0");
            Assert(!table.TryAssignIndex("example.sandbox", 1), "Must refuse hijacking vanilla index 1");
            // Index 2 belongs to standalone WeiXiangQi P4; Just Gone appends at index 3.
            Assert(table.TryAssignIndex("example.sandbox", 3), "Should assign index 3");

            // Vanilla indices 0 and 1 ALWAYS pass through
            AssertEqual(MenuRouteAction.PassThrough, table.Decide(0, "Friends", true, true).Action, "Vanilla 0 passes");
            AssertEqual(MenuRouteAction.PassThrough, table.Decide(1, "Free", true, true).Action, "Vanilla 1 passes");

            // WeiXiangQi at index 2 is NOT registered in Just Gone -> MUST PassThrough!
            AssertEqual(MenuRouteAction.PassThrough, table.Decide(2, "围象棋", true, true).Action, "WeiXiangQi index 2 passes");

            // Registered Just Gone mode at index 3 with matching title -> LaunchRegisteredMode!
            MenuRouteResult r3 = table.Decide(3, "Sandbox Mode", true, true);
            AssertEqual(MenuRouteAction.LaunchRegisteredMode, r3.Action, "Index 3 launches");
            AssertEqual("example.sandbox", r3.MatchedEntry.ModeId, "Matched mode id");

            // Title mismatch at index 3 -> PassThrough (safety guard against selector reorder)
            AssertEqual(MenuRouteAction.PassThrough, table.Decide(3, "Different Mod", true, true).Action, "Title mismatch passes");

            // Disabled mode blocks safely without falling through to vanilla
            jgMode.Enabled = false;
            AssertEqual(MenuRouteAction.BlockModeDisabled, table.Decide(3, "Sandbox Mode", true, true).Action, "Disabled blocks");
            jgMode.Enabled = true;

            // Framework not ready blocks
            AssertEqual(MenuRouteAction.BlockModeDisabled, table.Decide(3, "Sandbox Mode", false, true).Action, "Not ready blocks");

            // Scene loading in progress blocks
            AssertEqual(MenuRouteAction.BlockSceneLoading, table.Decide(3, "Sandbox Mode", true, false).Action, "Loading blocks");
        }

        private static void TestMenuRoutingMultiMod()
        {
            var table = new MenuRouteTable();
            table.Register("modA.m1", "modA", "Mode A1");
            table.Register("modA.m2", "modA", "Mode A2");
            table.Register("modB.m1", "modB", "Mode B1");
            table.TryAssignIndex("modA.m1", 3);
            table.TryAssignIndex("modA.m2", 4);
            table.TryAssignIndex("modB.m1", 5);

            bool duplicateThrew = false;
            try { table.Register("modA.m1", "modC", "Dup"); }
            catch (InvalidOperationException) { duplicateThrew = true; }
            Assert(duplicateThrew, "Duplicate mode id must throw");

            AssertEqual(2, table.UnregisterByOwner("modA"), "Unregister by owner removes modA modes");
            AssertEqual(MenuRouteAction.PassThrough, table.Decide(3, "Mode A1", true, true).Action, "Removed mode passes");
            AssertEqual(MenuRouteAction.LaunchRegisteredMode, table.Decide(5, "Mode B1", true, true).Action, "ModB remains active");
        }

        private static void TestSessionGuard()
        {
            AssertEqual(SessionGuardDecision.KeepRunning,
                SessionGuard.Decide(true, true, true, 10f, 1.5f), "Alive -> KeepRunning");
            AssertEqual(SessionGuardDecision.TeardownNormal,
                SessionGuard.Decide(false, false, false, 0f, 1.5f), "Unloading scene -> TeardownNormal");
            AssertEqual(SessionGuardDecision.WaitForSceneChange,
                SessionGuard.Decide(false, true, true, 0.5f, 1.5f), "Within grace -> Wait");
            AssertEqual(SessionGuardDecision.AbortFailsafe,
                SessionGuard.Decide(false, true, true, 1.6f, 1.5f), "Past grace in live scene -> AbortFailsafe");
        }

        private static void TestSaveTransactionRules()
        {
            Assert(SaveTransactionRules.ShouldRestore(7, 7), "CAS int match");
            Assert(!SaveTransactionRules.ShouldRestore(7, 0), "CAS int modified -> skip");
            Assert(SaveTransactionRules.ShouldRestore(false, false), "CAS bool match");
            Assert(!SaveTransactionRules.ShouldRestore(false, true), "CAS bool modified -> skip");

            AssertEqual(SaveTxnState.Recovered,
                SaveTransactionRules.Reconcile(SaveTxnState.Applying, 4, 0, 0, 4), "All written -> Recovered");
            AssertEqual(SaveTxnState.RolledBack,
                SaveTransactionRules.Reconcile(SaveTxnState.Prepared, 0, 4, 0, 4), "All before -> RolledBack");
            AssertEqual(SaveTxnState.SkippedNewerProgress,
                SaveTransactionRules.Reconcile(SaveTxnState.Applying, 3, 0, 1, 4), "Player touched key -> Skip");
            AssertEqual(SaveTxnState.Committed,
                SaveTransactionRules.Reconcile(SaveTxnState.Committed, 4, 0, 0, 4), "Terminal stays terminal");

            AssertEqual(SaveTxnState.SkippedNewerProgress,
                SaveTransactionRules.Parse(SaveTransactionRules.Describe(SaveTxnState.SkippedNewerProgress)),
                "State describe/parse round-trip");
        }

        private static void TestAtomicFileWriter()
        {
            string dir = Path.Combine(Path.GetTempPath(), "justgone_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "config.json");
                string bak = Path.Combine(dir, "config.json.bak");

                AtomicFileWriter.WriteAllTextAtomic(file, "{\"v\":1}", bak);
                AssertEqual("{\"v\":1}", File.ReadAllText(file, Encoding.UTF8), "Initial atomic write");

                AtomicFileWriter.WriteAllTextAtomic(file, "{\"v\":2}", bak);
                AssertEqual("{\"v\":2}", File.ReadAllText(file, Encoding.UTF8), "Atomic replace");
                AssertEqual("{\"v\":1}", File.ReadAllText(bak, Encoding.UTF8), "Backup preserves v1");

                // Lock target file exclusively so File.Replace fails and verify v2 survives + no orphan .tmp files
                bool threw = false;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try { AtomicFileWriter.WriteAllTextAtomic(file, "{\"v\":3}", bak); }
                    catch (IOException) { threw = true; }
                }
                Assert(threw, "Locked file replace must throw IOException");
                AssertEqual("{\"v\":2}", File.ReadAllText(file, Encoding.UTF8), "v2 intact after failed replace");
                AssertEqual(0, Directory.GetFiles(dir, "*.tmp").Length, "No temp files left behind");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void TestMiniJson()
        {
            var payload = new Dictionary<string, object>
            {
                ["name"] = "围象棋/JustGone",
                ["size"] = 19,
                ["active"] = true,
                ["items"] = new List<object> { 1, "星", false, null }
            };
            string json = MiniJson.Serialize(payload);
            Assert(MiniJson.TryParse(json, out object parsed), "Parse serialized json");
            AssertEqual("围象棋/JustGone", MiniJson.GetString(parsed, "name"), "String value");
            AssertEqual(19, MiniJson.GetInt(parsed, -1, "size"), "Int value");
            AssertEqual(true, MiniJson.GetBool(parsed, false, "active"), "Bool value");
            AssertEqual(4, MiniJson.GetArray(parsed, "items").Count, "Array length");
            Assert(!MiniJson.TryParse("{invalid", out _), "Malformed json fails TryParse");
        }

        private static void TestPieceLedger()
        {
            var ledger = new PieceLedger<string>(19);
            ledger.Set(1, new BoardCoord(3, 3), "BlackKing");
            ledger.Set(2, new BoardCoord(3, 4), "RedPawn");
            AssertEqual(2, ledger.Count, "Initial piece count");
            AssertEqual(0, ledger.VerifyIntegrity().Count, "Initial integrity");

            // Transition: remove piece #2 at (3,4), move piece #1 INTO vacated (3,4), add piece #3 at (9,9)
            var desired = new List<LedgerPiece<string>>
            {
                new LedgerPiece<string>(1, new BoardCoord(3, 4), "BlackKing"),
                new LedgerPiece<string>(3, new BoardCoord(9, 9), "WhiteStone")
            };
            List<PieceDeltaOp<string>> ops = ledger.PlanTransition(desired);
            AssertEqual(3, ops.Count, "Should plan 3 ops");
            AssertEqual(PieceDeltaOpKind.Remove, ops[0].Kind, "Remove must come first");
            AssertEqual(PieceDeltaOpKind.Move, ops[1].Kind, "Move must come second");
            AssertEqual(PieceDeltaOpKind.Add, ops[2].Kind, "Add must come third");

            ledger.ApplyOps(ops);
            AssertEqual(2, ledger.Count, "Final count");
            Assert(ledger.TryGetAt(new BoardCoord(3, 4), out LedgerPiece<string> moved) && moved.PieceId == 1, "Moved piece at vacated cell");
            Assert(!ledger.HasAt(new BoardCoord(3, 3)), "Old cell empty");
            AssertEqual(0, ledger.VerifyIntegrity().Count, "Post-transition integrity");
        }

        private static void TestDisposableTracker()
        {
            var order = new List<int>();
            var tracker = new DisposableTracker();
            tracker.Track(new ActionDisposable(() => order.Add(1)));
            tracker.Track(new ActionDisposable(() => { order.Add(2); throw new InvalidOperationException("boom"); }));
            tracker.Track(new ActionDisposable(() => order.Add(3)));

            int errors = 0;
            tracker.DisposeAll(_ => errors++);
            AssertEqual(1, errors, "One error captured without aborting remaining disposals");
            AssertEqual(3, order.Count, "All 3 disposables executed");
            AssertEqual(3, order[0], "LIFO order 3");
            AssertEqual(2, order[1], "LIFO order 2");
            AssertEqual(1, order[2], "LIFO order 1");
        }

        private static void TestInputLockRegistry()
        {
            var reg = new InputLockRegistry();
            Assert(!reg.IsLocked, "Initially unlocked");
            IDisposable lockA = reg.Acquire("modA", "dialog");
            IDisposable lockB = reg.Acquire("modB", "custom-mode");
            Assert(reg.IsLocked && reg.LockCount == 2, "Two locks active");

            lockA.Dispose();
            Assert(reg.IsLocked && reg.LockCount == 1, "ModB lock persists after ModA releases");

            reg.ReleaseAllByOwner("modB");
            Assert(!reg.IsLocked && reg.LockCount == 0, "Unlocked after ModB owner release");
            lockB.Dispose(); // idempotent
            Assert(!reg.IsLocked, "Idempotent dispose");
        }
    }
}
