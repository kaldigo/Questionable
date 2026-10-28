<!-- Authored with LLM assistance, changes must be reviewed and owned by a human.
Initial version reviewed and owned by @kaldigo -->
# Questionable with BossMod Reborn support

This maintained fork preserves Questionable's assembly, InternalName, commands, configuration,
upstream quest data and original authorship. It adds an isolated Reborn adapter. It is an
independent development preview, not an upstream-supported Questionable release.

Install the fork through `https://raw.githubusercontent.com/kaldigo/Questionable/new-main/pluginmaster.json`.
Use one Questionable copy and one BossMod variant. Existing configuration remains under Questionable.
Report fork issues to https://github.com/kaldigo/Questionable, not the upstream maintainers.

Select BossMod in the combat provider settings to use the loaded variant; the interface shows
Reborn when loaded. Wrath remains available for ordinary quest combat. Solo duties use the selected
BossMod family integration regardless of the ordinary combat provider, matching upstream behaviour.

## Integration boundaries

`Questionable/External/Reborn/` contains the Reborn-specific IPC and lifecycle. The existing
`BossModIpc` facade delegates to it when Reborn is loaded. Standard VBM retains its original path.
Both variants publish `BossMod.*` IPC, so automation refuses to start when both are loaded.
Reborn uses separate preset names, verifies preset imports by reading them back, checks activation,
and restores only settings this integration changed. Later user changes are retained.
No equivalent to VBM's obstacle-map clearing command exists in the audited Reborn release;
the map-clear button is disabled for Reborn. No fake command or global transient reset is sent.

The updater in `Questionable/PathData/PathDataUpdater.cs` is unchanged. It continues downloading
compatible upstream quest/gathering bundles, validating format and checksum as upstream does.
Plugin code updates and route updates remain separate.

## Upstream updates and releases

The daily scheduled GitHub workflow fetches `PunishXIV/Questionable:new-main` and proposes a merge.
Clean documentation, route JSON, numeric version updates, and eligible existing C# method-body
changes can merge automatically after checks. The syntax reviewer protects fork changes and
their transitive dependencies. Changes to Reborn, presets, solo duties, saved configuration,
route loading, dependencies, workflows, native interop or contracts remain open for review.
Conflicts remain proposals; the workflow never force-pushes the maintained branch.

Automatic routine merges explicitly dispatch the release workflow. Release recovery handles
a prior successful merge whose publication dispatch failed. Versions are allocated independently
of upstream's source version, and release assets retain the source commit. The fork feed points
only to its own immutable release archive. Upstream publishing workflows are guarded to run only
in the upstream repository. GitHub Actions must be enabled and allowed to create pull requests.

The fork does not require upstream publisher credentials or an ImageSharp licence key.
Game icons use native BGRA textures scaled by ImGui instead of ImageSharp CPU resampling.
This may slightly change small-icon sharpness. Upstream attribution and licence remain included.

The fork icon preserves the upstream Questionable arrows and location pin, recoloured through
image generation to the same teal-to-magenta palette as the maintained Wrath icon, without the
corner insignia. `assets/icon-source.png` retains the generated original and `assets/icon.png`
is the 512-pixel plugin export. The upstream reference is https://puni.sh/api/plugins/icon/94.

## Validation

Run `tools/build.ps1` with Dalamud API 15 references installed, or set `DALAMUD_HOME`.
The build runs Reborn state/preset contract checks, actual-Git upstream policy tests, and
upstream quest JSON schema tests. Packaging validates identity, runtime dependencies and feed.

Native acceptance is still required: ordinary quest combat, solo-duty entry/exit, phase transitions,
death/retry, cancellation, plugin reload, unavailable IPC, and user preset/setting changes.
The import check establishes schema retention, not equivalent encounter AI behaviour.
If cleanup reports failure, stop questing and inspect Reborn before resuming.
