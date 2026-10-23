using System;
using System.Collections.Generic;
using XingShu.Platform;

namespace PlatformProfileConformanceRunner
{
    class Program
    {
        private static int _passCount = 0;
        private static int _failCount = 0;

        static void Print(string msg)
        {
            Console.WriteLine("{0}", (object)msg);
        }

        static void Print(string fmt, params object[] args)
        {
            Console.WriteLine("{0}", (object)string.Format(fmt, args));
        }

        private static void Assert(bool condition, string scenarioName, string detail)
        {
            if (condition)
            {
                Print("[PASS] {0}", scenarioName);
                _passCount++;
            }
            else
            {
                Print("[FAIL] {0}: {1}", scenarioName, detail);
                _failCount++;
                throw new Exception(string.Format("Assertion failed in {0}: {1}", scenarioName, detail));
            }
        }

        static int Main(string[] args)
        {
            Print("================================================================================");
            Print("PROJECT XINGSHU HS4-A.1: C# PLATFORMPROFILE CROSS-LANGUAGE CONFORMANCE RUNNER");
            Print("================================================================================");

            try
            {
                // Scenario 1: STEAM_CN full VERIFIED profile PASS: all 17 fields valid, passes validate()
                RunScenario1();

                // Scenario 2: VERIFIED missing hash -> reject: set gameRuntimeDllSha256 = null on VERIFIED profile, assert error
                RunScenario2();

                // Scenario 3: NOT_YET_AUDITED null unknown fields -> PASS: TapTap / Bilibili / WeGame with null fields pass validation
                RunScenario3();

                // Scenario 4: PARTIALLY_AUDITED partial fields -> PASS: Steam Global with partial disk fields passes validation
                RunScenario4();

                // Scenario 5: empty PlatformId -> reject: "" fails closed
                RunScenario5();

                // Scenario 6: unknown PlatformId -> reject: "EPIC_GAMES" fails closed
                RunScenario6();

                // Scenario 7: real 32-hex bundleId -> PASS: "6cc68f014947b66dea74130802768a37" passes
                RunScenario7();

                // Scenario 8: fake UUID placeholder no longer needed: 32-hex accepted, non-32-hex rejected
                RunScenario8();

                // Scenario 9: Bilibili process = 吉星派对.exe (assert profile processExecutable == "吉星派对.exe")
                RunScenario9();

                // Scenario 10: WeGame process = 吉星派对.exe (assert profile processExecutable == "吉星派对.exe")
                RunScenario10();

                // Scenario 11: STEAM_GLOBAL = PARTIALLY_AUDITED (assert compatibilityStatus == "PARTIALLY_AUDITED")
                RunScenario11();

                // Scenario 12: Current baseline governance: verify_hs4_a_baseline_governance PASS (only STEAM_CN is VERIFIED; if any other is promoted without audit, governance fails closed)
                RunScenario12();

                // Scenario 13: Tri-Language Decoupling Conformance: Generic Validation PASS for fully verified STEAM_GLOBAL, but Baseline Governance FAILS
                RunScenario13Decoupling();

                Print("================================================================================");
                Print("C# CONFORMANCE RESULTS: {0} PASSED, {1} FAILED (TOTAL 13 SCENARIOS)", _passCount, _failCount);
                Print("================================================================================");
                return _failCount == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Print("\n[FATAL ERROR]: {0}", ex.Message);
                Print("================================================================================");
                Print("C# CONFORMANCE RESULTS: {0} PASSED, {1} FAILED", _passCount, _failCount + 1);
                Print("================================================================================");
                return 1;
            }
        }

        static void RunScenario1()
        {
            var p = PlatformProfile.GetProfile(PlatformId.STEAM_CN);
            Assert(p != null, "Scenario 1: STEAM_CN profile exists", "");
            Assert(p.platformId == "STEAM_CN", "Scenario 1: platformId == STEAM_CN", "");
            Assert(p.channel == "CN_STEAM", "Scenario 1: channel == CN_STEAM", "");
            Assert(p.region == "CN", "Scenario 1: region == CN", "");
            Assert(p.steamAppId == 2622000, "Scenario 1: steamAppId == 2622000", "");
            Assert(p.gameVersion == "3.3.0", "Scenario 1: gameVersion == 3.3.0", "");
            Assert(p.gameBuild == "3.3.0", "Scenario 1: gameBuild == 3.3.0", "");
            Assert(p.processExecutable == "AstralParty_CN.exe", "Scenario 1: processExecutable == AstralParty_CN.exe", "");
            Assert(!string.IsNullOrEmpty(p.installRoot), "Scenario 1: installRoot is set", "");
            Assert(p.gameRuntimeDllPath == "GameAssembly.dll", "Scenario 1: gameRuntimeDllPath == GameAssembly.dll", "");
            Assert(p.gameRuntimeDllSha256 == "6279B4A1814E9B0DEF32779EBC2DFD5810373F112F0B12EE02F5C97CAA4F46FA", "Scenario 1: gameRuntimeDllSha256 matches frozen hash", "");
            Assert(p.xingshuRuntimeDllSha256 == null, "Scenario 1: old runtime hash is cleared", "");
            Assert(!string.IsNullOrEmpty(p.bundleRelativePath), "Scenario 1: bundleRelativePath is set", "");
            Assert(p.bundleId == "3486f99276eba77677d4880fd8d32918", "Scenario 1: bundleId matches resource 026", "");
            Assert(p.containerSha256 == "420FBB1DEE1CEFE7298213EBF5F292B4722D0CB08A2A5E3F96EE94AB4AABDDEF", "Scenario 1: containerSha256 matches baseline", "");
            Assert(p.hookTextAssetPathId == 99992000L, "Scenario 1: hookTextAssetPathId == 99992000", "");
            Assert(p.hookSignatures != null && p.hookSignatures.Count == 4, "Scenario 1: hookSignatures has 4 signatures", "");
            Assert(p.compatibilityStatus == "PARTIALLY_AUDITED", "Scenario 1: updated resource awaits verification", "");
            Assert(p.lastVerifiedAt == null, "Scenario 1: old validation timestamp is cleared", "");

            bool valid = p.Validate(out string err);
            Assert(valid && err == null, "Scenario 1: STEAM_CN passes Validate()", err ?? "");
        }

        static void RunScenario2()
        {
            var p = PlatformProfile.GetProfile(PlatformId.STEAM_CN);
            p.compatibilityStatus = "VERIFIED";
            p.gameRuntimeDllSha256 = null;
            bool valid = p.Validate(out string err);
            Assert(!valid, "Scenario 2: Setting gameRuntimeDllSha256 to null on VERIFIED profile must fail Validate()", "");
            Assert(err != null && err.Contains("gameRuntimeDllSha256 must be set for VERIFIED profile"), "Scenario 2: Error message must specify gameRuntimeDllSha256", err ?? "");
        }

        static void RunScenario3()
        {
            var unverifiedIds = new[] { PlatformId.BILIBILI, PlatformId.WEGAME };
            foreach (var id in unverifiedIds)
            {
                var p = PlatformProfile.GetProfile(id);
                Assert(p != null, "Scenario 3: " + id + " profile exists", "");
                Assert(p.compatibilityStatus == "PARTIALLY_AUDITED" && p.installRoot != null && p.bundleRelativePath != null,
                    "Audited channel directory and resource are recorded: " + id, "");
                Assert(p.Validate(out string auditedError), "Audited profile validates: " + id, auditedError ?? "");
                // Exercise unknown fields on an independent fixture, not on the current channel evidence.
                p.compatibilityStatus = "NOT_YET_AUDITED";
                p.installRoot = p.gameVersion = p.gameBuild = p.gameRuntimeDllPath = null;
                p.gameRuntimeDllSha256 = p.xingshuRuntimeDllSha256 = p.bundleRelativePath = p.bundleId = p.containerSha256 = null;
                p.hookTextAssetPathId = null; p.hookSignatures = new List<string>(); p.lastVerifiedAt = null;
                Assert(p.compatibilityStatus == "NOT_YET_AUDITED", "Scenario 3: " + id + " status == NOT_YET_AUDITED", "");
                Assert(p.steamAppId == null, "Scenario 3: " + id + " steamAppId is null", "");
                Assert(p.installRoot == null, "Scenario 3: " + id + " installRoot is null", "");
                Assert(p.gameVersion == null, "Scenario 3: " + id + " gameVersion is null", "");
                Assert(p.gameBuild == null, "Scenario 3: " + id + " gameBuild is null", "");
                Assert(p.gameRuntimeDllPath == null, "Scenario 3: " + id + " gameRuntimeDllPath is null", "");
                Assert(p.gameRuntimeDllSha256 == null, "Scenario 3: " + id + " gameRuntimeDllSha256 is null", "");
                Assert(p.xingshuRuntimeDllSha256 == null, "Scenario 3: " + id + " xingshuRuntimeDllSha256 is null", "");
                Assert(p.bundleRelativePath == null, "Scenario 3: " + id + " bundleRelativePath is null", "");
                Assert(p.bundleId == null, "Scenario 3: " + id + " bundleId is null", "");
                Assert(p.containerSha256 == null, "Scenario 3: " + id + " containerSha256 is null", "");
                Assert(p.hookTextAssetPathId == null, "Scenario 3: " + id + " hookTextAssetPathId is null", "");
                Assert(p.hookSignatures == null || p.hookSignatures.Count == 0, "Scenario 3: " + id + " hookSignatures is empty", "");
                Assert(p.lastVerifiedAt == null, "Scenario 3: " + id + " lastVerifiedAt is null", "");

                bool valid = p.Validate(out string err);
                Assert(valid && err == null, "Scenario 3: " + id + " with null unknown fields passes Validate()", err ?? "");
            }
        }

        static void RunScenario4()
        {
            var p = PlatformProfile.GetProfile(PlatformId.STEAM_GLOBAL);
            Assert(p != null, "Scenario 4: STEAM_GLOBAL profile exists", "");
            Assert(p.compatibilityStatus == "PARTIALLY_AUDITED", "Scenario 4: status == PARTIALLY_AUDITED", "");
            Assert(p.steamAppId == 2622000, "Scenario 4: steamAppId == 2622000", "");
            Assert(!string.IsNullOrEmpty(p.installRoot), "Scenario 4: installRoot is set for STEAM_GLOBAL", "");
            Assert(p.gameRuntimeDllSha256 == "27A1215326741786D41E82A4335669727A3A6F6283EF2C025BF2C0A97002D7E2", "Scenario 4: gameRuntimeDllSha256 is set", "");
            Assert(p.bundleId == "8cc684d4b3b979e86b5788b7f1814132", "Scenario 4: bundleId is 8cc684d4b3b979e86b5788b7f1814132", "");
            Assert(p.bundleRelativePath == @"AstralParty_INT_Data\com.unity.addressables\AssetBundles\57a198703b66364d5420d39c0d251747\8cc684d4b3b979e86b5788b7f1814132\__data", "Scenario 4: bundleRelativePath matches official cache", "");
            Assert(p.containerSha256 == "20846D23A75E2F6CE78AE2FFFB661AA4CB02364C45AA27D99FA85FD4F6F3C103", "Scenario 4: containerSha256 matches official cache", "");
            Assert(p.hookTextAssetPathId == 99992000L, "Scenario 4: hookTextAssetPathId is 99992000", "");
            Assert(p.hookSignatures != null && p.hookSignatures.Count == 4, "Scenario 4: hookSignatures has 4 signatures", "");
            Assert(p.xingshuRuntimeDllSha256 == null, "Scenario 4: xingshuRuntimeDllSha256 is null", "");
            Assert(p.lastVerifiedAt == null, "Scenario 4: lastVerifiedAt is null", "");

            bool valid = p.Validate(out string err);
            Assert(valid && err == null, "Scenario 4: STEAM_GLOBAL with partial fields passes Validate()", err ?? "");

            var pt = PlatformProfile.GetProfile(PlatformId.TAPTAP);
            Assert(pt != null, "Scenario 4: TAPTAP profile exists", "");
            Assert(pt.compatibilityStatus == "PARTIALLY_AUDITED", "Scenario 4: TAPTAP status == PARTIALLY_AUDITED", "");
            Assert(pt.steamAppId == null, "Scenario 4: TAPTAP steamAppId is null", "");
            Assert(pt.installRoot == @"F:\TapTap\PC Games\524022\CN_TAPTAP_V3.3.0", "Scenario 4: TAPTAP installRoot matches", "");
            Assert(pt.processExecutable == "吉星派对.exe", "Scenario 4: TAPTAP processExecutable == 吉星派对.exe", "");
            Assert(pt.gameRuntimeDllSha256 == "BAE3828CEF31104E4B77856115E7106FB8539FCC0A0A6098B57A34E9354044E6", "Scenario 4: TAPTAP gameRuntimeDllSha256 matches", "");
            Assert(pt.bundleId == "71c9197f5dd65c9b30bdf1e716719991", "Scenario 4: TAPTAP bundleId matches resource 026", "");
            Assert(pt.containerSha256 == "EA45C939DBC86ACA6950AC757FCABB773A86F9D14FEF6B3D8ED7C49F47D33598", "Scenario 4: TAPTAP containerSha256 matches baseline", "");
            Assert(pt.hookTextAssetPathId == 99992000L, "Scenario 4: TAPTAP hookTextAssetPathId is 99992000", "");
            Assert(pt.hookSignatures != null && pt.hookSignatures.Count == 4, "Scenario 4: TAPTAP hookSignatures has 4 signatures", "");
            bool validTap = pt.Validate(out string errTap);
            Assert(validTap && errTap == null, "Scenario 4: TAPTAP with partially audited fields passes Validate()", errTap ?? "");
        }

        static void RunScenario5()
        {
            bool threw = false;
            try
            {
                PlatformProfile.ParsePlatformId("");
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Assert(threw, "Scenario 5: Empty PlatformId throws ArgumentException (fails closed)", "");

            var p = PlatformProfile.GetProfile(PlatformId.STEAM_CN);
            p.platformId = "";
            bool valid = p.Validate(out string err);
            Assert(!valid && err.Contains("platformId cannot be empty"), "Scenario 5: Validate() rejects empty platformId", err ?? "");
        }

        static void RunScenario6()
        {
            bool threw = false;
            try
            {
                PlatformProfile.ParsePlatformId("EPIC_GAMES");
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Assert(threw, "Scenario 6: Unknown PlatformId 'EPIC_GAMES' throws ArgumentException (fails closed)", "");

            var p = PlatformProfile.GetProfile(PlatformId.STEAM_CN);
            p.compatibilityStatus = "NOT_YET_AUDITED";
            p.platformId = "EPIC_GAMES";
            bool valid = p.Validate(out string err);
            Assert(!valid && err.Contains("Unknown platformId"), "Scenario 6: Validate() rejects unknown platformId", err ?? "");
        }

        static void RunScenario7()
        {
            var p = PlatformProfile.GetProfile(PlatformId.STEAM_CN);
            p.bundleId = "6cc68f014947b66dea74130802768a37";
            bool valid = p.Validate(out string err);
            Assert(valid && err == null, "Scenario 7: Real 32-hex bundleId passes Validate()", err ?? "");
        }

        static void RunScenario8()
        {
            var p = PlatformProfile.GetProfile(PlatformId.STEAM_CN);

            // 32-hex accepted
            p.bundleId = "6cc68f014947b66dea74130802768a37";
            Assert(p.Validate(out _), "Scenario 8: 32-hex accepted", "");

            // Fake UUID placeholder with hyphens (length 36) rejected
            p.bundleId = "c2a8f8e1-5b74-4b57-9d7a-18f4a9b6c001";
            bool validUuid = p.Validate(out string errUuid);
            Assert(!validUuid && errUuid.Contains("32-character hex"), "Scenario 8: Fake UUID placeholder rejected", errUuid ?? "");

            // Arbitrary non-32-hex string rejected
            p.bundleId = "not-a-valid-hex-bundle-id";
            Assert(!p.Validate(out _), "Scenario 8: Non-32-hex string rejected", "");

            // Short hex rejected
            p.bundleId = "6cc68f014947";
            Assert(!p.Validate(out _), "Scenario 8: Short hex rejected", "");
        }

        static void RunScenario9()
        {
            var p = PlatformProfile.GetProfile(PlatformId.BILIBILI);
            Assert(p != null, "Scenario 9: BILIBILI profile exists", "");
            Assert(p.processExecutable == "吉星派对.exe", "Scenario 9: Bilibili process == 吉星派对.exe", p.processExecutable ?? "null");
        }

        static void RunScenario10()
        {
            var p = PlatformProfile.GetProfile(PlatformId.WEGAME);
            Assert(p != null, "Scenario 10: WEGAME profile exists", "");
            Assert(p.processExecutable == "吉星派对.exe", "Scenario 10: WeGame process == 吉星派对.exe", p.processExecutable ?? "null");
        }

        static void RunScenario11()
        {
            var p = PlatformProfile.GetProfile(PlatformId.STEAM_GLOBAL);
            Assert(p != null, "Scenario 11: STEAM_GLOBAL profile exists", "");
            Assert(p.compatibilityStatus == "PARTIALLY_AUDITED", "Scenario 11: STEAM_GLOBAL compatibilityStatus == PARTIALLY_AUDITED", "");
            Assert(p.CompatibilityStatusEnum == CompatibilityStatus.PARTIALLY_AUDITED, "Scenario 11: CompatibilityStatusEnum == PARTIALLY_AUDITED", "");
        }

        static void RunScenario12()
        {
            var defaultProfiles = PlatformProfile.GetDefaultProfiles();
            Assert(defaultProfiles != null && defaultProfiles.Count == 5, "Scenario 12: Exactly 5 default profiles loaded", "");
            Assert(defaultProfiles[0].platformId == "STEAM_CN", "Scenario 12: Profile 0 is STEAM_CN", "");
            Assert(defaultProfiles[1].platformId == "STEAM_GLOBAL", "Scenario 12: Profile 1 is STEAM_GLOBAL", "");
            Assert(defaultProfiles[2].platformId == "TAPTAP", "Scenario 12: Profile 2 is TAPTAP", "");
            Assert(defaultProfiles[3].platformId == "BILIBILI", "Scenario 12: Profile 3 is BILIBILI", "");
            Assert(defaultProfiles[4].platformId == "WEGAME", "Scenario 12: Profile 4 is WEGAME", "");
            bool govPass = PlatformProfile.VerifyHs4ABaselineGovernance(defaultProfiles, out string govErr);
            Assert(govPass && govErr == null, "Scenario 12: Baseline governance passes for default profiles", govErr ?? "");

            // Promoting any other platform to VERIFIED fails closed
            foreach (var unverifiedId in new[] { "STEAM_GLOBAL", "TAPTAP", "BILIBILI", "WEGAME" })
            {
                var tampered = PlatformProfile.GetDefaultProfiles();
                foreach (var prof in tampered)
                {
                    if (prof.platformId == unverifiedId) prof.compatibilityStatus = "VERIFIED";
                }
                bool tamperedPass = PlatformProfile.VerifyHs4ABaselineGovernance(tampered, out string tamperedErr);
                Assert(!tamperedPass && tamperedErr.Contains("Governance violation"), "Scenario 12: Promoting " + unverifiedId + " fails governance", tamperedErr ?? "");
            }

            // Resource updates may clear earlier verification without fabricating a new audit.
            var noVerified = PlatformProfile.GetDefaultProfiles();
            foreach (var prof in noVerified)
            {
                if (prof.platformId == "STEAM_CN") prof.compatibilityStatus = "PARTIALLY_AUDITED";
            }
            bool noVerPass = PlatformProfile.VerifyHs4ABaselineGovernance(noVerified, out string noVerErr);
            Assert(noVerPass && noVerErr == null, "Scenario 12: Awaiting verification passes governance", noVerErr ?? "");
        }

        static void RunScenario13Decoupling()
        {
            // Construct a temporary STEAM_GLOBAL profile with full fields and compatibilityStatus = VERIFIED
            var promotedGlobal = new PlatformProfile
            {
                platformId = "STEAM_GLOBAL",
                channel = "INT_STEAM",
                region = "GLOBAL",
                steamAppId = 2622000,
                gameVersion = "3.3.0",
                gameBuild = "3.3.0",
                processExecutable = "AstralParty.exe",
                installRoot = @"C:\Program Files (x86)\Steam\steamapps\common\AstralParty",
                gameRuntimeDllPath = "GameAssembly.dll",
                gameRuntimeDllSha256 = "27A1215326741786D41E82A4335669727A3A6F6283EF2C025BF2C0A97002D7E2",
                xingshuRuntimeDllSha256 = "B8CC3264CC36B33DA6B28930F7854223160B39B09991F17917DEDD6A41D7927F",
                bundleRelativePath = @"AstralParty_Data\__data",
                bundleId = "6cc68f014947b66dea74130802768a37",
                containerSha256 = "105E39705202C5DC125063D2A9412FB0573E61213906C731827F14660C996C10",
                hookTextAssetPathId = 99992000L,
                hookSignatures = new List<string> { "XingShuBootstrap.EnsureRuntimeHost", "SpectatorEventInterceptor.Initialize" },
                compatibilityStatus = "VERIFIED",
                lastVerifiedAt = "2026-10-17T00:00:00Z"
            };

            // 1. Generic Validation: MUST PASS
            bool valid = promotedGlobal.Validate(out string error);
            Assert(valid && error == null, "Scenario 13: Generic Validation for fully verified STEAM_GLOBAL passes", error ?? "");

            // 2. Baseline Governance (standalone list): MUST FAIL
            bool govPassSolo = PlatformProfile.VerifyHs4ABaselineGovernance(new List<PlatformProfile> { promotedGlobal }, out string govErrSolo);
            Assert(!govPassSolo, "Scenario 13: Standalone verified STEAM_GLOBAL fails baseline governance", govErrSolo ?? "");

            // 3. Baseline Governance (in default profiles collection): MUST FAIL
            var profiles = PlatformProfile.GetDefaultProfiles();
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].platformId == "STEAM_GLOBAL")
                {
                    profiles[i] = promotedGlobal;
                    break;
                }
            }
            bool govPass = PlatformProfile.VerifyHs4ABaselineGovernance(profiles, out string govErr);
            Assert(!govPass && govErr != null && govErr.Contains("Governance violation"), "Scenario 13: Baseline governance with verified STEAM_GLOBAL fails closed", govErr ?? "");
        }
    }
}
