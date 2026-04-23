# Upstream Boundary (2026-04-24)

## Purpose

This repository is the generic peer-symmetric relay engine/reference repo.
Real lessons from `D:\cardgame-dad-relay` and `D:\Unity\card game` are useful,
but only a subset is upstream-safe.

## Promote Here

Promote a lesson here when it is:

- relay-core behavior
- peer-symmetric by construction
- reusable outside the card-game product
- explainable without Unity or one product's operator surface

Typical examples:

- generic relay doctor checks
- peer-symmetric adapter/cost/policy improvements
- compact relay health artifacts that are not product-specific
- reusable packet/state validation helpers

## Do Not Promote Here

Keep these out of this repo by default:

- Unity-specific operator flows
- card-game route/governance logic
- product dashboards
- product prompt wording
- product retry heuristics
- product evidence schemas tied to one runtime

## Porting Rule

When copying an idea from `D:\cardgame-dad-relay`:

1. strip product names and paths
2. check whether the rule still makes sense with only generic relay concepts
3. keep only the reusable engine seam
4. leave the operator/product wrapping downstream

## Relationship To The Templates

- `autopilot-template` should own outer-loop/operator-control lessons
- `dad-v2-system-template` should own reusable DAD runtime/session lessons
- this repo should own reusable relay-engine/reference-integration lessons

If a change needs all three layers to make sense, split it before upstreaming.
