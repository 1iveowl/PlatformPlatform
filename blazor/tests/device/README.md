# Device pass runner (spike, EP-187)

Runs cells of the device pass on real Safari on the macOS host, outside the development container. Node's built-in
modules only; nothing is installed on the Mac.

## One-time setup on the Mac

1. `safaridriver --enable` (asks for the admin password once).
2. In the editor's Ports view, change the local address of the forwarded gateway port 9000 to 19000, and keep the
   mail server port 9005 forwarded. The runner then owns 9000 on the Mac's loopback and refuses the network by
   closing it. Change it back to 9000 for everyday use.
3. `bash blazor/tests/device/probe.sh` reports the versions and what is installed; it changes nothing.

## Run

From the repository folder on the Mac, with a Production publish served (see the blazor-publish skill):

```bash
node blazor/tests/device/safari-offline.mjs
node blazor/tests/device/safari-push.mjs
```

Results, screenshots and the probe report go to `.workspace/blazor-tests/device/`, stamped with the commit, the
publish identity, the served worker version and the macOS, Safari and driver versions.

## What it cannot do on Safari

- Set a notification permission: the driver answers `POST /permissions` with "not implemented", and an automation
  session starts with notifications granted, so denial and revocation stay with the container harness.
- See a real push reach the worker: the push service accepts it, but the automation session's worker never shows it.
