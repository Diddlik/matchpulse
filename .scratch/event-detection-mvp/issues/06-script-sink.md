# 06: Script sink

**What to build:** The Player can configure scripts that run for every Game Event and receive the event JSON on stdin, so OBS or any other tool can be driven without MatchPulse knowing about it.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] Scripts are configured in the config file next to webhooks
- [ ] Each Game Event starts each configured script once, with the event JSON on stdin
- [ ] A failing, hanging or slow script or webhook neither blocks detection nor delays other sinks, and the failure is logged
