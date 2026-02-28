# Phase 0 - Execution Checklist (Start Here)

Bu checklist Faz 0'in "plan" olmaktan cikmasi icin kullanilir.

## Owners (assign before work starts)
- Security owner: `TBD`
- Platform/Ops owner: `TBD`
- Backend owner: `TBD`
- Legal owner: `TBD`
- Sales enablement owner: `TBD`

## Week 1 Checklist (minimum)

### A. Control Inventory Freeze
- [ ] `PHASE0_CONTROL_INVENTORY.md` satirlari owner bazinda gozden gecirildi
- [ ] Her satira `Owner` atandi
- [ ] `Needs Verification` satirlar icin dogrulama tarihi yazildi (internal tracking)

### B. Evidence Inventory Baseline
- [ ] `PHASE0_EVIDENCE_INVENTORY.md` icin evidence vault lokasyonu belirlendi
- [ ] Naming/redaction kurallari onaylandi
- [ ] Ilk 5 kritik artifact yakalandi:
- [ ] EV-AUD-001 (audit integrity)
- [ ] EV-OPS-004 (kill switch)
- [ ] EV-OPS-005 (replay)
- [ ] EV-BKP-002 (restore dry-run)
- [ ] EV-PAY-001 (payment webhook idempotency)

### C. Claim Governance Baseline
- [ ] `PHASE0_CLAIM_REGISTER.md` security + legal + sales tarafindan review edildi
- [ ] `Blocked` ve `Conditional` ifadeler sales ekibine bildirildi
- [ ] Onaysiz claim kullanimi icin escalation owner belirlendi

### D. Certification Gap Snapshot
- [ ] `PHASE0_CERTIFICATION_GAP_MATRIX_SOC2.md` management ile paylasildi
- [ ] SOC 2 Type I target quarter/timeline karari alindi (veya explicitly deferred)
- [ ] Pentest ve DR tabletop karar sahipleri atandi

## Evidence Capture Workflow (each artifact)
1. Artifact capture
2. Redaction
3. Store in evidence vault
4. Update `PHASE0_EVIDENCE_INVENTORY.md` status
5. Peer review (security/platform)

## Exit Criteria (Phase 0 Complete)
- [ ] Control inventory owner-atamali ve status-guncel
- [ ] Evidence inventory icin en az 60% `Available/Captured`
- [ ] Claim register onayli ve sales tarafina dagitilmis
- [ ] SOC 2 readiness gap matrix management tarafindan kabul edilmis
- [ ] Faz 1 backlog (Trust Center + Security Overview + questionnaires + DPA) tarihli

## Notes / Decisions Log
- YYYY-MM-DD:
- YYYY-MM-DD:
