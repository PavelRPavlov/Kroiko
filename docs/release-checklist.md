# Release checklist

How a version of the offline PWA (`Kroiko.Client.Blazor`) goes to production, and the manual checks no
automated test covers ([ADR-0007](adr/0007-parity-and-test-strategy.md) §9). The tests cover the conversion,
parity with the Server under `bg-BG` and `en-US`, the offline start in Chromium, and saving with a stubbed
folder picker. What only a person can check is real Edge, the installed window, the real folder picker,
updates between two deployed versions, the production host, and real orders.

Three parts, in the order you use them:

1. **[Release procedure](#1-release-procedure):** every release. Bump the version, tag it, deploy with
   `scripts/publish-pwa.ps1`, then open the sign-off issue.
2. **[Every production release](#2-every-production-release-10-minutes)** (~10 minutes): the checks on the
   installed Edge PWA.
3. **[Go-live](#3-go-live-once)** (once, with `v1.0.0`): the production host, the browsers, and a side-by-side
   run of real orders through the Server and the PWA, before operators get the address.

Each production release gets a GitHub issue **"Release vX.Y.Z sign-off"**. Paste in part 2, and for `v1.0.0`
part 3 too. Tick each box as you check it, or write "n/a" and the reason next to it. Close the issue when every
box is done. For `v1.0.0`, operators get `https://app.kroiko.com` only after that issue is closed.

> **The repository and its issues are public.** Real Polyboard files, the order files made from them, and
> customer names stay on the local machine. Never commit them, attach them to an issue or paste them into
> one. In the issue, refer to orders by number only ("order 3: Suliver, 23 fields, 5 materials"). Never
> paste the deployment token anywhere. The operator's environment holds it as `SWA_CLI_DEPLOYMENT_TOKEN`.

## 1. Release procedure

Deploys are manual. There is no CI. `scripts/publish-pwa.ps1` enforces the branch model
([ADR-0001](adr/0001-host-pwa-on-azure-static-web-apps.md), [07](implementation/07-hosting-and-go-live.md) 07a.2):
production is deployed only from `release`, from a pushed tag `vX.Y.Z` that equals the csproj `<Version>`, and
never from an older version than the newest tag on `origin`.

### Once per deploying machine

- PowerShell 7.2+, the .NET SDK from `global.json`, and the Azure Static Web Apps CLI
  (`npm i -g @azure/static-web-apps-cli`).
- The Playwright Chromium, because the script runs the full `dotnet test`, E2E included. If it is missing, the
  first failing E2E test prints the exact `playwright.ps1 install chromium` command.
- The script needs a clean tree, including untracked files. Add local tool folders such as `.claude/` to
  `.git/info/exclude`, not to `.gitignore`.

### Steps

1. **Bump the version.** Open a PR to `main` that raises `<Version>` in
   `Kroiko.Client.Blazor/Kroiko.Client.Blazor.csproj` above the newest `vX.Y.Z` tag (`1.0.0` at go-live). Merge it.
   ADR-0002 §6 covers the version.
2. **Merge `main` into `release` and push it.**

   ```powershell
   git fetch origin
   git switch release
   git merge --ff-only origin/release
   git merge origin/main
   git push origin release
   ```

   `release` is also the branch of the Server's deploy workflow, `.github/workflows/release_kroiko.yml`. Its
   `push` trigger is commented out today, so this push does not deploy the Server. If that trigger has been
   turned back on, a push that changes `ATAFurniture.Server/**` redeploys the Server too. Check before you push.
3. **Tag the release and push the tag.**

   ```powershell
   git tag -a vX.Y.Z -m "vX.Y.Z"
   git push origin vX.Y.Z
   ```

4. **Rehearse** with `-DryRun`. It runs every check, the full `dotnet test` and the publish, then prints the
   `swa deploy` command, the file count and the size instead of deploying. It needs neither the token nor the
   CLI.

   ```powershell
   ./scripts/publish-pwa.ps1 -Environment production -DryRun
   ```

   It must end with `Dry run: would deploy vX.Y.Z (<sha>) to production`. If it refuses, fix what it names;
   never work around it.
5. **Deploy.** Enter the token in your own session only.

   ```powershell
   $env:SWA_CLI_DEPLOYMENT_TOKEN = Read-Host -MaskInput 'SWA deployment token'
   ./scripts/publish-pwa.ps1 -Environment production
   ```

   It runs the checks and the tests again, then deploys. Success is the line
   `Deployed vX.Y.Z (<sha>) to production: <url>`. Note `vX.Y.Z (<sha>)` for the issue. The generated
   `*.azurestaticapps.net` URL is never given to anyone (ADR-0001).
6. **Open the sign-off issue.** Title: "Release vX.Y.Z sign-off". Paste in part 2 (and part 3 for `v1.0.0`)
   and record the deployed `vX.Y.Z (<sha>)`. Run the checks, tick them and close the issue.

Optional: before step 2, the `main` staging environment can be tried with
`./scripts/publish-pwa.ps1 -Environment main`, which needs `HEAD` = `origin/main`. Staging is a different
origin, so installs and settings made there are separate from production.

### A bad release

Fix it forward ([ADR-0002](adr/0002-pwa-updates-reload-prompt.md) §8). Revert or fix the change in a PR to
`main`, bump `<Version>` again, and follow the steps above. The fix reaches installed apps through the normal
update prompt.

- **Never** redeploy an older build. The script refuses a version older than the newest tag on `origin`.
- Never move, delete or re-push a pushed tag. Never force-push `release`.
- If a deploy fails part-way, running the script again with the same, newest version is allowed. Deploys are
  atomic, so a failed one leaves the previous version in place.

## 2. Every production release (~10 minutes)

On the **installed Edge PWA** from `https://app.kroiko.com`, on a machine that ran the previous version.

Record: `vX.Y.Z (<sha>)` · date · who · Edge version (`edge://version`).

> **`v1.0.0` has no earlier production build**, so there is nothing to update from. Mark the "Update" items
> n/a for `v1.0.0`. Part 3 installs `v1.0.0` fresh, and the update flow is first checked at the next release.

### Update ([ADR-0002](adr/0002-pwa-updates-reload-prompt.md), [06](implementation/06-updates-and-about.md))

**Before you deploy:** open the installed Kroiko (the previous version). Start it a second time from the Start
menu to get a second window. In the first window, upload a Polyboard file and do not save anything, so that its
Order is unsaved.

- [ ] Within about an hour of the deploy, both windows show the snackbar "Нова версия е налична" with
      "Презареди" and "По-късно". It appears sooner when a window is minimised and restored, or when the
      connection returns.
- [ ] In the window with the unsaved Order, "Презареди" first asks "Текущата поръчка ще бъде изгубена. Да
      презаредя ли с новата версия?". "Не" keeps the Order and the offer.
- [ ] "По-късно" hides the snackbar. About (the ⓘ icon, "Относно") then shows "Нова версия е налична" with
      "Презареди".
- [ ] About's "Презареди", then "Да", reloads into the new version. About shows the `vX.Y.Z (<sha>)` that the
      script printed.
- [ ] In the second window, "Презареди" reloads it into the new version too. It has no unsaved Order, so there
      is no question.
- [ ] About's "Провери за обновления" reports "Използвате най-новата версия.". With the network disconnected,
      it reports "Няма връзка със сървъра. Опитайте отново по-късно.". Reconnect afterwards.

### Conversion and saving ([ADR-0003](adr/0003-save-order-files-to-picked-folder.md), [05](implementation/05-saving.md))

The synthetic fixtures in `Kroiko.Testing/TestData/polyboard/` are enough here. For example:
`wardrobes-4-materials.txt`, `cabinet-23-field.txt` and `bathroom-4-materials.txt`.

- [ ] One conversion per manufacturer: Лонира, Съливер and Мега Трейдинг. For each: upload, fill "Контакти на
      клиента", then "Генерирай бланки за поръчка". The generated files are listed.
- [ ] "Запази в папка…" is the filled (primary) button, and "Изтегли всички" the outlined one.
- [ ] "Запази в папка…" opens the real Windows folder picker. No test drives it (ADR-0007 §5). Pick an empty
      folder: every listed file is written there. The confirmation "Файловете са записани в папка „…“:" lists
      their names.
- [ ] Save the same Order again. The picker opens at that folder, and nothing is overwritten: the new copies are
      named `… (2).xlsx` (and so on), and the confirmation lists those names.
- [ ] Cancelling the picker does nothing.
- [ ] "Изтегли всички" downloads every file. The first time on a device, Edge asks whether to allow multiple
      downloads: allow it. Admins can pre-allow it with Edge's `DefaultAutomaticDownloadsSetting`.
- [ ] Clicking one file's name downloads only that file.

### Offline

- [ ] Close every Kroiko window, disconnect the network, then start Kroiko. It starts, and About shows the new
      version. One conversion and "Изтегли всички" work. Reconnect afterwards.

## 3. Go-live (once)

Run with `v1.0.0` before any operator is given the address ([07](implementation/07-hosting-and-go-live.md) 07b).
Pavel plus one operator.

Record: date · who · PWA `v1.0.0 (<sha>)` · Edge version · the deployed Server's host (Windows or Linux) and the
commit it was built from.

### Production host

These are the 07a.4 staging checks, run again on `app.kroiko.com`. Repeat them whenever
`staticwebapp.config.json` changes.

```powershell
$site = 'https://app.kroiko.com'
$assets = curl.exe -s "$site/service-worker-assets.js"
$wasm, $dat, $font = foreach ($ext in 'wasm', 'dat', 'woff2') { [regex]::Match("$assets", ('"url": "([^"]+\.{0})"' -f $ext)).Groups[1].Value }
curl.exe -sI -H 'Accept-Encoding: br' "$site/$wasm"
foreach ($path in 'manifest.webmanifest', $dat, $font) { curl.exe -sI "$site/$path" }
foreach ($path in 'configuration', 'no-such-page', '_framework/x.js', '_content/MudBlazor/x.css') { curl.exe -sI "$site/$path" }
foreach ($path in '', 'index.html', 'service-worker.js', 'service-worker-assets.js') { curl.exe -sI "$site/$path" }
```

- [ ] `https://app.kroiko.com` opens over HTTPS with a valid certificate. It is the only address given to
      operators, never the generated `*.azurestaticapps.net` one (ADR-0001).
- [ ] The `.wasm` file returns `Content-Encoding: br` and `Content-Type: application/wasm`.
- [ ] `manifest.webmanifest` returns `application/manifest+json`, the `.dat` file `application/octet-stream`,
      and the font `font/woff2`.
- [ ] `/configuration` and `/no-such-page` return `200` (the app). `/_framework/x.js` and
      `/_content/MudBlazor/x.css` return `404`.
- [ ] `/`, `/index.html`, `/service-worker.js` and `/service-worker-assets.js` carry `Cache-Control: no-cache`.
      A deep link such as `/configuration` does not, because SWA applies no route rules to a fallback response
      (07a.1). That is expected.

### Install and browsers

- [ ] In Edge, install Kroiko from `https://app.kroiko.com` ("App available" in the address bar → Install). It
      opens in its own window. Close it, disconnect the network, and start it: it starts offline.
- [ ] In an Edge tab (not the installed app), "Запази в папка…" writes all files. The second save opens at the
      last folder, never overwrites, and lists the final names. Part 2 covers the installed window.
- [ ] In an Edge InPrivate window, save with "Запази в папка…" twice. The second save reads the last folder back
      from IndexedDB, which crashed the page in Chromium 153's off-the-record profiles in the tests
      ([05](implementation/05-saving.md) step 2). Record whether the page crashed. If it did, open an issue
      naming the Edge version.
- [ ] Blocked picker: on a machine whose Edge policy `DefaultFileSystemWriteGuardSetting` is `2` (block),
      "Запази в папка…" shows "Браузърът не позволява запис в папка. Изтеглете файловете с „Изтегли всички“.".
      "Изтегли всички" still works. Remove the policy afterwards.
- [ ] In Firefox (a tab; it cannot install the app), "Изтегли всички" is the only save button, and it is the filled
      one. It downloads every file, and one file's link downloads that file.

### Before the side-by-side run

- [ ] Phase 01's Excel review ([01](implementation/01-golden-baseline.md) step 3). In the PWA, use the contacts
      `Тест ООД` / `0888123456` to convert `wardrobes-4-materials.txt` for Лонира, `cabinet-23-field.txt` for
      Съливер and `bathroom-4-materials.txt` for Мега Трейдинг. The E2E parity tests prove these files equal
      the golden files. Open each `.xlsx` in Excel: it opens without a repair prompt, and its values, Cyrillic
      text and layout look right.
- [ ] If the deployed Server includes phase 02 (rewired onto `Kroiko.Domain`), its UI smoke check has passed:
      one fixture per manufacturer matches the golden files ([02](implementation/02-shared-domain.md) step 5).
      If the deployed Server predates phase 02, it is the Server the golden files recorded, and the run below
      compares against it directly.

### Side-by-side run on real orders ([ADR-0007](adr/0007-parity-and-test-strategy.md) §9)

Pick ~10 recent real orders. Keep the files in a local folder only (see the note at the top).

- [ ] The orders cover all three manufacturers, both field formats (11 and 23 fields) and Cyrillic material
      names. List them in the issue by number, manufacturer, field count and material count.
- [ ] Every Polyboard file is UTF-8 ([ADR-0006](adr/0006-known-conversion-bugs-in-pwa.md) §5). Both apps read
      only UTF-8, so a Windows-1251 file would give the same wrong names in both, and parity alone would not
      show it. Also check Polyboard's export settings for an encoding option, and record it. If a file is not
      UTF-8, **stop**: supporting another encoding needs a new ADR.

      ```powershell
      $strict = [System.Text.UTF8Encoding]::new($false, $true)
      foreach ($file in Get-ChildItem <folder> -Filter *.txt) {
          try { $null = $strict.GetString([System.IO.File]::ReadAllBytes($file.FullName)); "$($file.Name): UTF-8" }
          catch { "$($file.Name): NOT UTF-8" }
      }
      ```

- [ ] Run each order through the deployed Server and through the installed PWA, with the same manufacturer, the
      same contacts and the same edits, on the same day (Suliver and MegaTrading names carry the date).
- [ ] Both apps make the same files under the same names. The PWA's names pass through `FileNameSanitizer`: a
      character from `\ / : * ? " < > |` becomes `_`, as a browser download does on the Server (CONTEXT.md §9).
- [ ] Every `.xlsx` is identical: its worksheets match byte for byte. The zip's timestamps may differ. This
      prints nothing when they match:

      ```powershell
      function Get-WorksheetHashes([string] $Xlsx) {
          $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Xlsx).Path)
          try {
              foreach ($entry in $zip.Entries | Where-Object FullName -like 'xl/worksheets/*.xml') {
                  $stream = $entry.Open()
                  try { "$($entry.FullName) $((Get-FileHash -InputStream $stream -Algorithm SHA256).Hash)" } finally { $stream.Dispose() }
              }
          } finally { $zip.Dispose() }
      }
      Compare-Object (Get-WorksheetHashes <server.xlsx>) (Get-WorksheetHashes <pwa.xlsx>)
      ```

- [ ] Every `.cut_mt` is byte-equal apart from the date. Today's `.cut_mt` carries no date, so expect `fc.exe /b
      <server.cut_mt> <pwa.cut_mt>` to print `FC: no differences encountered`. Write `fc.exe`: in PowerShell,
      `fc` is `Format-Custom`. If only CR bytes (`0D`) differ, the Server writes LF, so it is a Linux-hosted build
      without [ADR-0009](adr/0009-cut-mt-line-endings-crlf.md). The go-live needs a Server with it, so stop and
      settle it with Pavel.
- [ ] Cyrillic material names show correctly in both apps' grids and in the files.
- [ ] The files open in each manufacturer's software: Lonira's and Suliver's `.xlsx`, and MegaTrading's `.xlsx`
      and the CRLF `.cut_mt` in MegaTrading's (ADR-0009).

### Intended differences ([CONTEXT.md §9](../CONTEXT.md))

The PWA differs from the Server on purpose in these rows. Check that each one behaves as listed.

- [ ] No login, account, credits or email. Nothing leaves the device: in DevTools (F12) → Network, converting
      and saving send nothing to any server. Only local `blob:` downloads appear, plus at most the app's own
      files from `app.kroiko.com`.
- [ ] `bad-lines-12.txt` (a fixture) is rejected. The alert lists 10 lines as `ред N: …`, then "…и още 2", and
      keeps the link to the configuration page. Nothing is loaded. The Server shows a generic alert.
- [ ] `kitchen-8-materials.txt` for Мега Трейдинг names the 8 materials and cannot be generated. Merging
      materials with the rename in "Използвани материали" down to 6 enables "Генерирай бланки за поръчка". The
      Server generates it with a truncated `.cut_mt` header.
- [ ] Both contact fields are required to generate. After a generation, restart the app: the contacts and the
      manufacturer are pre-filled.
- [ ] In "Използвани материали", renaming A → B and B → A, then "Замести използваните с новите материали",
      swaps the two materials. It does not merge them.
- [ ] After generating, any edit (a cell, a contact field) clears the generated files without asking.
- [ ] With files present, switching the manufacturer or uploading again asks "Файловете за поръчка и всички
      редакции по тях ще бъдат изгубени. Да продължа ли?". "Не" keeps the Order.
- [ ] Going to Configuration and back to Converter keeps the Order.
- [ ] Files are saved to a picked folder or downloaded, with ` (n)` on a clash and sanitised names (part 2 and
      the side-by-side run). The Server gives Blob Storage links instead.
- [ ] No spinner stays on during the whole run, and every error showed a snackbar.

### Sign-off

- [ ] Every box in the issue is ticked, or marked n/a with its reason.
- [ ] Close "Release v1.0.0 sign-off". Only then are operators given `https://app.kroiko.com`. The Server stays
      deployed, unchanged.
