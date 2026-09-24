"""Filesystem-level checks for Delta's local preparation hook."""

import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
PREPARE = ROOT / ".agents/prepare"
HOOK = runpy.run_path(str(PREPARE))


class PrepareTests(unittest.TestCase):
    def setUp(self):
        scratch = ROOT / ".jiangyu"
        scratch.mkdir(exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="prepare-test-", dir=scratch)
        self.addCleanup(self.temporary.cleanup)
        base = Path(self.temporary.name)
        self.source = base / "original checkout"
        self.destination = base / "nested" / "new checkout"
        self.jiangyu = base / "jiangyu"
        (self.jiangyu / "src/Jiangyu.Cli").mkdir(parents=True)
        for directory in (self.source, self.destination):
            directory.mkdir(parents=True)
            self.write(directory, "jiangyu.json", '{"name":"WOMENACE"}')
            subprocess.run(["git", "init", "-q", str(directory)], check=True)
        self.git("remote", "add", "local", str(self.source / ".git"))
        self.environment = {
            key: value for key, value in os.environ.items()
            if key not in ("WOMENACE_PREPARE_SOURCE", "JIANGYU_DIR")
        }

    def git(self, *arguments):
        subprocess.run(["git", "-C", str(self.destination), *arguments], check=True)

    @staticmethod
    def write(root, relative, text="cached"):
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        return path

    def prepare(self, **environment):
        result = subprocess.run(
            [sys.executable, str(PREPARE)],
            cwd=self.destination,
            env={**self.environment, **environment},
            capture_output=True, text=True, timeout=10,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout

    def populate(self):
        files = (
            ".jiangyu/unity_build/prefab.bundle",
            ".jiangyu/unity_build/womenace__audio",
            ".jiangyu/build-state.json",
            ".jiangyu/restored_bundles/restored.bundle",
            "unity/Assets/Imported/soldier/model.fbx",
            "code/local.props",
            "code/bin/Release/WOMENACE.Code.dll",
            ".jiangyu/code_build_state",
            "unity/Assets/Authored/doll/default/body.blend1",
            "unity/Assets/Authored/doll/default/body.blend1.meta",
            "unity/Assets/Authored/doll/_donor/rig.fbx",
            "unity/Assets/Authored/doll/_donor/rig.fbx.meta",
        )
        for relative in files:
            path = self.write(self.source, relative)
            os.utime(path, ns=(1_600_000_000_000_000_000, 1_600_000_000_000_000_000))
        return files

    def test_copies_only_selected_state_with_independent_files(self):
        files = self.populate()
        excluded = (
            "compiled/bundles/old.bundle", "unity/Library/import.db",
            "unity/Assets/Jiangyu/Staging/audio.wav", ".jiangyu/config.json",
            ".jiangyu/glb_staging/plan.json", ".env", "scripts/.config/private.json",
        )
        for relative in excluded:
            self.write(self.source, relative)
        self.prepare()
        for relative in files:
            original, copied = self.source / relative, self.destination / relative
            self.assertEqual(copied.read_bytes(), original.read_bytes())
            self.assertEqual(copied.stat().st_mtime_ns, original.stat().st_mtime_ns)
            self.assertFalse(os.path.samefile(original, copied))
            copied.write_text("worktree edit")
            self.assertEqual(original.read_text(), "cached")
        for relative in excluded:
            self.assertFalse((self.destination / relative).exists(), relative)
        config = (self.destination / "mise.local.toml").read_text()
        self.assertIn(str(self.jiangyu), config)

    def test_repeat_preserves_worktree_changes_and_configuration(self):
        files = self.populate()
        self.prepare()
        for relative in files:
            self.write(self.destination, relative, "local edit")
        self.write(self.destination, "mise.local.toml", "# local settings\n")
        self.prepare()
        for relative in files:
            self.assertEqual((self.destination / relative).read_text(), "local edit")
        self.assertEqual((self.destination / "mise.local.toml").read_text(), "# local settings\n")

    def test_does_not_attach_source_stamp_to_existing_outputs(self):
        self.populate()
        self.write(self.destination, ".jiangyu/unity_build/local.bundle", "local")
        self.write(self.destination, "code/bin/Release/local.dll", "local")
        self.prepare()
        for stamp in (".jiangyu/build-state.json", ".jiangyu/code_build_state"):
            self.assertFalse((self.destination / stamp).exists())

    def test_incomplete_source_group_is_not_copied(self):
        self.write(self.source, ".jiangyu/build-state.json")
        self.write(self.source, "code/bin/Release/WOMENACE.Code.dll")
        self.prepare()
        self.assertFalse((self.destination / ".jiangyu/build-state.json").exists())
        self.assertFalse((self.destination / "code/bin/Release").exists())

    def test_no_local_remote_is_a_noop(self):
        self.git("remote", "remove", "local")
        self.assertIn("skipping", self.prepare())
        self.assertFalse((self.destination / ".jiangyu").exists())
        self.assertFalse((self.destination / "mise.local.toml").exists())

    def test_missing_or_nonlocal_source_is_a_noop(self):
        for source in ("/missing/WOMENACE/.git", "https://example.invalid/WOMENACE.git"):
            with self.subTest(source=source):
                self.git("remote", "set-url", "local", source)
                self.assertIn("skipping", self.prepare())

    def test_accepts_file_url_with_spaces(self):
        self.git("remote", "set-url", "local", (self.source / ".git").as_uri())
        self.write(self.source, "code/local.props")
        self.prepare()
        self.assertTrue((self.destination / "code/local.props").is_file())

    def test_source_override_and_self_source(self):
        self.git("remote", "remove", "local")
        self.write(self.source, "code/local.props")
        self.prepare(WOMENACE_PREPARE_SOURCE=str(self.source))
        self.assertTrue((self.destination / "code/local.props").is_file())
        self.assertIn("skipping", self.prepare(WOMENACE_PREPARE_SOURCE=str(self.destination)))

    def test_explicit_jiangyu_directory_wins(self):
        self.prepare(JIANGYU_DIR="/my/jiangyu")
        self.assertFalse((self.destination / "mise.local.toml").exists())

    def test_existing_sibling_jiangyu_wins(self):
        (self.destination.parent / "jiangyu").mkdir()
        self.prepare()
        self.assertFalse((self.destination / "mise.local.toml").exists())

    def test_nested_source_symlink_becomes_an_independent_copy(self):
        original = self.write(self.source, "bundle-payload")
        directory = self.source / ".jiangyu/restored_bundles"
        directory.mkdir(parents=True)
        (directory / "linked.bundle").symlink_to(original)
        self.prepare()
        copied = self.destination / ".jiangyu/restored_bundles/linked.bundle"
        self.assertFalse(copied.is_symlink())
        copied.write_text("changed")
        self.assertEqual(original.read_text(), "cached")

    def test_refuses_writes_through_destination_symlink(self):
        self.write(self.source, "code/local.props")
        self.write(self.source, ".jiangyu/restored_bundles/test.bundle")
        (self.destination / ".jiangyu").symlink_to(self.source / ".jiangyu")
        with self.assertRaisesRegex(RuntimeError, "symlink"):
            HOOK["seed"](
                self.source, self.destination, ("code/local.props",), ["cp", "-pRL"],
            )

    def test_refuses_linked_destination_parent(self):
        self.write(self.source, "unity/Assets/Imported/soldier/model.fbx")
        shared = self.source / "shared"
        shared.mkdir()
        (self.destination / "unity").symlink_to(shared)
        with self.assertRaisesRegex(RuntimeError, "symlink"):
            HOOK["seed"](
                self.source, self.destination, ("unity/Assets/Imported",), ["cp", "-pRL"],
            )
        self.assertEqual(list(shared.iterdir()), [])

    def test_rechecks_destinations_after_copying(self):
        self.write(self.source, "code/local.props")
        run = subprocess.run

        def copy_then_edit(*args, **kwargs):
            result = run(*args, **kwargs)
            self.write(self.destination, "code/local.props", "local edit")
            return result

        with mock.patch.object(HOOK["subprocess"], "run", side_effect=copy_then_edit):
            HOOK["seed"](
                self.source, self.destination, ("code/local.props",), ["cp", "-pRL"],
            )
        self.assertEqual((self.destination / "code/local.props").read_text(), "local edit")

    def test_failed_copy_does_not_publish_a_stamp_or_partial_output(self):
        self.populate()
        group = (".jiangyu/unity_build", ".jiangyu/build-state.json")
        with self.assertRaises(subprocess.CalledProcessError):
            HOOK["seed"](
                self.source, self.destination, group,
                [sys.executable, "-c", "raise SystemExit(1)"],
            )
        for relative in group:
            self.assertFalse((self.destination / relative).exists())
        self.assertEqual(list((self.destination / ".jiangyu").iterdir()), [])


if __name__ == "__main__":
    unittest.main()
