# Version comparison, checked by hand

There is no JS test runner in this project, so the cases `isNewer` has to get right are written down
here rather than asserted. If a runner is ever added, this file is the test.

| latest | running | newer? | why it matters |
|---|---|---|---|
| `0.0.6` | `0.0.5` | yes | the ordinary case |
| `0.0.5` | `0.0.5` | no | must not nag someone who is up to date |
| `0.0.5` | `0.0.6` | no | a downgrade is not an update |
| `0.0.10` | `0.0.9` | **yes** | compared as strings this is false, and would hide exactly the release somebody needs |
| `0.1.0` | `0.0.99` | yes | minor beats patch |
| `1.0.0` | `0.9.9` | yes | major beats everything |
| `0.0.5` | `0.0.5-beta.1` | yes | the release beats the beta that led to it |
| `0.0.5-beta.2` | `0.0.5-beta.1` | yes | betas order among themselves |
| `0.0.5-beta.1` | `0.0.5` | no | nothing beats a release of the same number |

Checked against the release workflow's own numbering rules in `.github/workflows/ci.yml`, which is
where `v0.0.5-beta.1` and `v0.0.5` come from.
