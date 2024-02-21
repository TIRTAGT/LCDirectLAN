# LCDirectLAN - Change Logs

LCDirectLAN is a mod for Lethal Company built around BepInEx that fixes and enhances LAN lobbies without interfering with the Steam-networked lobbies.

The change logs are organized in reverse chronological order, with the latest release at the top.
Dates are in the format of `YYYY-MM-DD` (Year-Month-Day).

----

## [1.1.0] - 2024-02-21
- IPv6 hosting and join support
- AAAA DNS Record support for IPv6, SRV support for IPv4 and IPv6 (Adjustable Priority in config)
- Fix invisible join settings window after accidentally clicking host weekly challange
- Fix LatencyRPC timing issues, add alternate latency source
- Fix wrong NetworkManager instance check
- Migrate to .NET Standard 2.1
- Avoid SocketTImeout crash
- Fix HideJoinData doesn't protect config leak
- Add option to disable Latency HUD when hosting
- Use Late Inject to further guard messing up Online mode

## [1.0.0] - 2024-02-09
- Initial release