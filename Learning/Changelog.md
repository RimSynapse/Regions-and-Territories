# Changelog

Full version history for RimSynapse - Regions and Territories. The mod page and Workshop description show only the latest release; every earlier version is recorded here.

## v0.7.1 - Region generation, map modes and performance
- NEW - Region generation overhaul: provinces now grow terrain-aware and value-budgeted, following rivers, coastlines and mountains for prettier, more natural borders instead of arbitrary straight lines, and region size is bounded so no single province swallows the map.
- NEW - Map modes: a faction-shaded Territories view and a Population / dwellings view, drawn through the Map Mode Framework.
- NEW - Region-border overlay: an owner-coloured border overlay in the main Draw Settings toggles that works over any map mode.
- NEW - Region comparison panels: modifier-click a region (Ctrl or Shift, rebindable in the mod settings) to open a draggable readout; open several at once to compare, each titled by its unique region number.
- Performance: region aggregates - population, ownership, perimeter and border shares - are materialised and cached instead of recomputed every frame, and world-object bucketing is now O(n), so large worlds draw and tick far cheaper.
- Changed: the influence pie opens on region selection rather than on stationary hover; an optional setting shows ownership calculation breakdowns in tooltips without Dev Mode.
- Fixed: the settings "Detected:" integration line drew over the Faction Geography panel.
- Fixed: a settlement-validity check queried the player faction during world generation and spammed the log with errors.
- The new region generation applies to newly generated worlds; a new colony is recommended for the full effect.

## v0.7.0 - Regions and Territories Compatibility
- NEW - Mod-agnostic world object integration: Empire Refactored, Vanilla Outposts Expanded, Vanilla Expanded Framework and World Domination are recognised through adapter profiles instead of by name, so territory rules no longer hardcode which mods exist.
- NEW - Placement and territory governance: one evaluator decides where settlements, outposts, military installations and camps may stand, and the world inspect pane tells you why a tile was refused.
- NEW - Compatibility mode: a world generated before this mod is adopted rather than refused, and you are told when that happens.
- Fixed: Empire settlement population always read as zero. Three of the four adapter profiles named members or types that do not exist on the real assemblies, and nothing said so - a wrong name cost no error, it just returned a plausible zero.
- Fixed: Vanilla Expanded Framework renamed its assembly from VFECore to VEF, so that profile had resolved to nothing for as long as it had existed.
- Changed: the demographic component of territory ownership contributes nothing this release. It was awarding a fifth of the score for simply owning a settlement, which the settlement score already counted. It returns in 0.8 reading real regional demographics.
- REQUIRES A NEW COLONY - not save-game compatible.

## v0.6.2
- No gameplay changes. This release exists so the save-compatibility notice reaches people who already have the mod installed.
- Regions and Territories has always needed a new colony, and the Workshop page has said so - but the in-game description did not, so anyone who subscribed and never went back to the page had no way to find out.
- The warning now appears here, on the Workshop page, and in the compatibility matrix, and it says the same thing in all three.
- Coming in 0.7: worlds saved by 0.7 will not read correctly if you go back to 0.6 - the new regional resource stocks are dropped and mined-out provinces come back full. Loading an existing 0.6 world in 0.7 is fine and stays fine.

## v0.6.1
- Fixed: the in-game mod list showed v0.5.2 with no 0.6.0 notes; version and changelog now agree everywhere.
- Roadmap updated: 0.7 is Regions and Territories compatibility (groundwork for Factions, which will require Empire). Everything after it shifts up one release.

## v0.6.0
- Moves in step with RimSynapse Core v0.6.0 (Agent and Tool Foundation).
- Requires Core v0.6.0; saves and settings carry over unchanged.
- In-game wiki guides updated; "MCP" renamed to game tools throughout.
