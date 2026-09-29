# NPViera Android

This branch is the migration foundation from the PSVVPN pilot to the NPViera Android product baseline.

## Architecture boundaries
- UI
- Application
- Domain
- VPN Engine
- Transport
- Identity
- Management Client
- Persistence
- Diagnostics

The VPN endpoint, policy and identity lifecycle must not be permanently hard-coded in UI code. Connection behavior is represented by a single state machine and important failures use stable NPV error codes.

## Current migration
Phase 1 establishes domain models, connection state, diagnostics and configuration validation before moving the existing TunnelService behind the new VPN/Transport boundary.
