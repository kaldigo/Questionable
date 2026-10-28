# Authored with LLM assistance, changes must be reviewed and owned by a human.
# Initial version reviewed and owned by @kaldigo
"""Exercise the update policy against actual divergent Git histories."""
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('upstream_policy', ROOT/'tools/upstream_policy.py')
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)

class UpstreamPolicyTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT/'obj/policy-tests'
        parent.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=parent)
        self.addCleanup(self.temp.cleanup)
        self.previous = Path.cwd()
        os.chdir(self.temp.name)
        self.addCleanup(os.chdir, self.previous)
        self.git('init', '-b', 'main')
        self.git('config', 'user.name', 'Fork tests')
        self.git('config', 'user.email', 'tests@example.invalid')
        self.write('Directory.Build.targets', '<Project><PropertyGroup><Version>1.2.3.4</Version></PropertyGroup></Project>')
        self.source = 'namespace Sample; public class Helper { public int Shared() => 1; public int Ordinary() => 2; }'
        self.write('Questionable/Utils/Helper.cs', self.source)
        self.write('Questionable/External/BossModIpc.cs', 'namespace Sample; public class Adapter { public int Start() => 1; }')
        self.write('README.md', 'Upstream\n')
        self.git('add', '.'); self.git('commit', '-m', 'Shared')
        self.common = self.git('rev-parse', 'HEAD')
        self.write('Questionable/Fork.cs', 'namespace Sample; public class Fork { public int Run() => new Helper().Shared(); }')
        self.git('add', '.'); self.git('commit', '-m', 'Fork')
        self.base = self.git('rev-parse', 'HEAD')

    def git(self, *args):
        return subprocess.check_output(['git', *args], text=True, stderr=subprocess.DEVNULL).strip()

    def write(self, path, text):
        p=Path(path);p.parent.mkdir(parents=True, exist_ok=True);p.write_text(text)

    def propose(self, path, text):
        self.git('switch', '--detach', self.common)
        self.write(path, text);self.git('add', '.');self.git('commit', '-m', 'Upstream')
        self.incoming=self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base);self.git('merge', '--no-edit', self.incoming)
        return policy.classify(self.base, self.incoming, 'HEAD')

    def test_clean_route_updates_are_routine(self):
        self.assertTrue(self.propose('QuestPaths/route.json', '{}')['routine'])

    def test_documentation_is_routine(self):
        self.assertTrue(self.propose('README.md', 'Updated upstream\n')['routine'])

    def test_numeric_version_increase_is_routine(self):
        self.assertTrue(self.propose('Directory.Build.targets', '<Project><PropertyGroup><Version>1.2.3.5</Version></PropertyGroup></Project>')['routine'])

    def test_dependency_changes_require_review(self):
        self.assertFalse(self.propose('Questionable/Questionable.csproj', '<Project/>')['routine'])

    def test_adapter_changes_require_review(self):
        self.assertFalse(self.propose('Questionable/External/BossModIpc.cs', 'namespace Sample; public class Adapter { public int Start() => 2; }')['routine'])

    def test_route_loader_changes_require_review(self):
        self.assertFalse(self.propose('Questionable/PathData/PathDataUpdater.cs', 'namespace Sample; class Loader {}')['routine'])

    def test_workflow_changes_require_review(self):
        self.assertFalse(self.propose('.github/workflows/release.yml', 'name: changed')['routine'])

    def test_existing_unrelated_method_body_is_routine(self):
        result=self.propose('Questionable/Utils/Helper.cs', self.source.replace('Ordinary() => 2', 'Ordinary() => 3'))
        self.assertTrue(result['routine'], result['reasons'])

    def test_transitive_fork_dependency_requires_review(self):
        result=self.propose('Questionable/Utils/Helper.cs', self.source.replace('Shared() => 1', 'Shared() => 3'))
        self.assertFalse(result['routine'])

    def test_source_contract_change_requires_review(self):
        self.assertFalse(self.propose('Questionable/Utils/Helper.cs', self.source.replace('Ordinary()', 'Ordinary(int x)'))['routine'])

    def test_extra_changes_outside_recorded_merge_are_rejected(self):
        self.propose('README.md', 'Updated upstream\n')
        self.write('extra.txt', 'Unexpected');self.git('add', '.');self.git('commit', '-m', 'Unexpected')
        self.assertFalse(policy.classify(self.base, self.incoming, 'HEAD')['routine'])

if __name__ == '__main__':
    unittest.main()
