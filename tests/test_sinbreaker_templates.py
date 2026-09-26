"""Check Sinbreaker's inherited behaviour and tooltip links after `mise compile`."""

import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def value(node):
    kind = node["kind"]
    if kind == "TemplateReference":
        return node["reference"]["templateId"]
    return node.get(kind[0].lower() + kind[1:])


class SinbreakerTemplates(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        manifest = json.loads((ROOT / "compiled/templates.json").read_text())
        cls.patches = {p["templateId"]: p["set"] for p in manifest["templatePatches"]}

    def test_only_bunker_clears_inherited_airborne_damage_exclusion(self):
        for weapon in ["gun", "rocket", "drill"]:
            changes = [op for op in self.patches["active.voymastina_mech_" + weapon]
                       if op["fieldPath"] == "TargetCannotHaveOneOfTheseTags"]
            if weapon != "drill":
                self.assertEqual([], changes)
                continue
            self.assertEqual(1, len(changes))
            self.assertEqual("Clear", changes[0]["op"])
            self.assertEqual([{"field": "EventHandlers", "index": 0}], changes[0]["descent"])

    def test_custom_handlers_replace_inherited_behaviour(self):
        for perk, handler in [("monarch", "SinbreakerMonarch"), ("arc_rounds", "SinbreakerArcRounds"),
                              ("breach_guard", "SinbreakerBreachGuard")]:
            operations = self.patches["perk.voymastina_" + perk]
            clear = next(i for i, op in enumerate(operations) if op["op"] == "Clear" and op["fieldPath"] == "EventHandlers")
            handlers = [op for op in operations[clear + 1:] if op["fieldPath"] == "EventHandlers"]
            self.assertEqual(1, len(handlers))
            self.assertEqual("WOMENACE:" + handler, handlers[0]["value"]["typeConstruction"]["typeName"])

    def test_description_links_resolve(self):
        def strings(node):
            if isinstance(node, dict):
                if node.get("kind") == "String":
                    yield node["string"]
                for child in node.values():
                    yield from strings(child)
            elif isinstance(node, list):
                for child in node:
                    yield from strings(child)

        # Verified against the native TextTooltipsConfig, resources.assets 128694.
        native = {
            "action_points", "armor", "armor_damage", "armor_durability",
            "armor_penetration", "damage_reduction", "hitpoint_damage", "hitpoints",
            "range", "rate_of_fire",
        }
        custom = {}
        for op in self.patches["text_tooltips_config"]:
            if op["op"] != "Append" or op["fieldPath"] != "Tooltips":
                continue
            fields = {p["fieldPath"]: value(p["value"])
                      for p in op["value"]["composite"]["operations"]}
            if fields["LinkId"].startswith("voymastina_"):
                self.assertNotIn(fields["LinkId"], custom)
                custom[fields["LinkId"]] = fields["TooltipText"]
        self.assertEqual({"voymastina_target_lock", "voymastina_energy_thief"}, set(custom))

        descriptions = list(strings(custom))
        for prefix, names in [
                ("perk.voymastina_", ["monarch", "arc_rounds", "breach_guard"]),
                ("effect.voymastina_", ["target_lock", "breach_guard"]),
                ("active.voymastina_mech_", ["gun", "rocket", "drill"])]:
            for name in names:
                descriptions.extend(strings(self.patches[prefix + name]))
        links = set()
        for text in descriptions:
            found = re.findall(r'<link="([^"]+)">', text)
            self.assertEqual(text.count("<link="), len(found), text)
            self.assertEqual(len(found), text.count("</link>"), text)
            links.update(found)
        self.assertLessEqual(links, native | set(custom))
        self.assertLessEqual(set(custom), links)


if __name__ == "__main__":
    unittest.main()
