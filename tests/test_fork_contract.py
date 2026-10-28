# Authored with LLM assistance, changes must be reviewed and owned by a human.
# Initial version reviewed and owned by @kaldigo
"""Protect upstream route downloads and fork distribution boundaries."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

class ForkContractTests(unittest.TestCase):
    def test_upstream_routes_keep_original_destination_and_compatibility_checks(self):
        updater = (ROOT/'Questionable/PathData/PathDataUpdater.cs').read_text(encoding='utf-8-sig')
        for required in ['"/PunishXIV/Questionable"', 'manifest.IsCompatibleWith(PathDataFormat.CurrentVersion)',
                         'manifest.BundleSha256', 'AllowedHosts.Contains', '_configuration.PathData.AutoUpdate']:
            self.assertIn(required, updater)

    def test_reborn_keeps_shared_ipc_namespace_and_separate_identity(self):
        backend = (ROOT/'Questionable/External/Reborn/RebornBossModBackend.cs').read_text()
        self.assertIn('p.InternalName == "BossModReborn"', backend)
        self.assertIn('"BossMod.Presets.Get"', backend)
        self.assertIn('"/bmrai off"', backend)
        self.assertNotIn('"/vbm ', backend)
        self.assertNotIn('ClearTransient', backend)

    def test_routes_and_upstream_publishing_are_separate(self):
        for name in ['release.yml', 'publish-paths.yml', 'pr-test.yml']:
            self.assertIn("github.repository == 'PunishXIV/Questionable'", (ROOT/'.github/workflows'/name).read_text())
        sync = (ROOT/'.github/workflows/sync-upstream.yml').read_text()
        self.assertIn('upstream new-main', sync)
        self.assertIn('Merge only a routine update that passed every check', sync)

    def test_no_private_imagesharp_build_key_is_required(self):
        project = (ROOT/'Questionable/Questionable.csproj').read_text(encoding='utf-8-sig')
        self.assertNotIn('SixLabors', project)

if __name__ == '__main__':
    unittest.main()
