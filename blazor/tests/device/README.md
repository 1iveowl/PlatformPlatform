# Device pass runner

Runs the cells of the device pass that need a real browser engine on a real operating system: real Safari on macOS and
Safari in the iOS Simulator, both driven through `safaridriver` on the macOS host, outside the development container.
Node's built-in modules only; nothing is installed on the Mac to run it.

## One-time setup on the Mac

1. `safaridriver --enable` (asks for the admin password once).
2. In the editor's Ports view, change the local address of the forwarded gateway port 9000 to 19000, and keep the
   mail server port 9005 forwarded. The runner then owns 9000 on the Mac's loopback and refuses the network by
   closing it. Change it back to 9000 for everyday use.
3. Only for the iOS Simulator target, which does not work yet (see below): Xcode at `/Applications/Xcode.app` with an
   iOS Simulator runtime and an iPhone device for it. The runtime is several GB:
   `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer xcodebuild -downloadPlatform iOS`. The runner points its own
   processes at Xcode through `DEVELOPER_DIR`, so the Mac's active developer folder may stay the command line tools; set
   `DEVELOPER_DIR` to use another Xcode.
4. `bash blazor/tests/device/probe.sh` reports the versions and what is installed; it changes nothing.

## Run

From the repository folder on the Mac, with a Production publish served (see the blazor-publish skill):

```bash
node blazor/tests/device/run.mjs
```

It runs the probe and then the offline shell and push on real Safari on macOS, prints one verdict and writes it to
`device-pass.json`. `--target simulator` runs the offline shell in the iOS Simulator instead and `--target all` runs
both; `--device "<name>"` picks the iPhone device. Each script also runs on its own:

```bash
node blazor/tests/device/safari-offline.mjs --target mac
node blazor/tests/device/safari-offline.mjs --target simulator
node blazor/tests/device/safari-push.mjs
```

Results, screenshots and the probe report go to `.workspace/blazor-tests/device/`, stamped with the commit, the
publish identity, the served worker version, and the device, operating system and browser versions of the target.

## Verdicts

- **passed**: every case of every run passed.
- **passed with manual cells**: the only cases that did not pass are cells a script cannot establish on that browser,
  each named with its reason in the result. A manual cell is decided from what the browser or its driver answered in the
  run, never assumed in advance, and it is never counted as passed.
- **failed**: a case failed, a run could not start, or a result is missing, stale or ran fewer cases than expected.

## The iOS Simulator target does not work yet

Observed 2026-09-23 on macOS 27.0 (26A428) with Safari 27.0 (22625.1.29.11.27), Xcode 27.0 (27A266a) selected and the
iOS 27.0 runtime (24A434): the runner boots the iPhone device and adds the development certificate to its trust store,
but `safaridriver` refuses every session with "Could not find any session hosts that match the requested capabilities",
with or without `safari:deviceUDID`, `safari:deviceType`, `safari:deviceName` or `safari:platformVersion`, and with
Safari already running in the simulator. Its `--diagnose` log records only the refusal. The target stays in the runner,
off by default, for the follow-up that settles it.

What the target does to the simulator:

It boots the newest installed iOS runtime's iPhone device when it is shut down, and shuts it down again afterwards. It
adds the certificate the gateway presents, which is the self-signed development certificate, to that simulator's trust
store with `xcrun simctl keychain <device> add-root-cert`, and keeps a copy as `development-authority.pem` beside the
results. Nothing on the Mac itself changes. The simulator shares the Mac's loopback, so it reaches the runner's proxy and
nothing listens beyond the loopback.

## What it cannot do on Safari

- Set a notification permission: the driver answers `POST /permissions` with "not implemented", and an automation
  session starts with notifications granted. The revocation case is recorded as manual; the container harness covers
  denial and revocation.
- See the notification a real push shows. The push service delivers it and Safari fires the worker's push event, but the
  automation session lists no notification: in the runs so far Safari handed the push to the connection without a data
  store identifier, not to one with an identifier. The case reads Safari's push log for the send
  (`safari-push-delivery.log`) and is manual only when that log shows the push event fired and completed; otherwise it
  fails. A person confirms the notification on screen.
- Push on iOS: web push there needs an app added to the Home Screen, which a driver does not reach.
