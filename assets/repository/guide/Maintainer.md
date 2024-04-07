# LCDirectLAN - Maintainer Guide

## New release checklist
- [ ] Copy this file somewhere else as a checklist, so checking off items doesn't trigger a changed file in git
- [ ] Bump versions in:
  - [ ] `/LCDirectLan.cs`
  - [ ] `/assets/thunderstore/manifest.json`

- [ ] Make a new change log entry in `/CHANGELOG.md`
- [ ] Duplicate the `CHANGELOG.md` entry to `/assets/thunderstore/CHANGELOG.md`
- [ ] Make a new commit to the development branch with the changes above as "Prepare for vX.X.X release"
- [ ] Push that commit to the development branch
- [ ] Make a new pull request to main from the development branch, title should be `vX.X.X`, where `X.X.X` is the version number of the release and the description should be a change log entry (look at the previous pull requests for examples)
- [ ] Merge the pull request with the merge title `vX.X.X (#A)`, where `X.X.X` is the version number and `#A` is the pull request number, and the description should be `Check the pull request for change logs.`
- [ ] Copy the whole `/assets/thunderstore/` folder to a different location to prepare for the release, from now on any mentiong of `$RELEASE_DIR` will be this location instead of the repository folder
- [ ] Rename `$RELEASE_DIR` folder to this format: `TIRTAGT-LCDirectLAN-X.X.X`, where `X.X.X` is the version number
- [ ] Build the source code with `dotnet build -c Release`
- [ ] Move the built `/bin/Release/netstandard2.1/LCDirectLAN.dll` to `$RELEASE_DIR/BepInEx/plugins/LCDirectLAN.dll`
- [ ] Move the `/bin/Release/netstandard2.1/libs/*` contents to `$RELEASE_DIR/BepInEx/plugins/libs/`
- [ ] Delete `$RELEASE_DIR/BepInEx/plugins/libs/README.md`
- [ ] Delete `$RELEASE_DIR/BepInEx/plugins/README.md`
- [ ] Archive the `$RELEASE_DIR` folder contents as a `.zip` file named `TIRTAGT-LCDirectLAN-X.X.X.zip`, where `X.X.X` is the version number
- [ ] Create a new GitHub release and upload the `.zip` file as the binary for that release
- [ ] Upload the `.zip` file to Thunderstore
- [ ] Done!