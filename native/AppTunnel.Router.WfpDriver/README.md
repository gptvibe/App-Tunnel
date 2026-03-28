# AppTunnel.Router.WfpDriver

Planned native Windows Filtering Platform callout driver.

## Intent

- Classify and steer traffic for selected applications
- Enforce production-grade routing policy beyond the WinDivert MVP
- Provide a stable control surface for the user-mode bridge and service

## TODO

- Select the WDK project layout and minimum supported Windows build
- Define callout layers, provider context, and policy update model
- Implement signing and installer registration workflow
- Add validation around DNS behavior, reconnect semantics, and leak prevention