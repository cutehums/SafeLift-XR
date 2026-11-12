# Limitations

## Code-only Unity snapshot

This repository does not include the complete Unity project. Scene serialization, prefab references, `.meta` GUIDs, UI resources, audio, fonts, Android configuration, avatar assets and third-party packages are absent. The C# files document implementation structure but cannot reproduce the original application on their own.

## External dependencies

Some C# scripts reference Meta XR SDK, TextMesh Pro, XR Interaction Toolkit, Input System and Humanoid avatar components. Their packages and scene bindings must be restored in a licensed local Unity project.

## Runtime mismatch retained from the source snapshot

`backend/main.py` contains a `run_mock_session()` helper, but the current argument parser does not register the mock-session CLI arguments expected by the older runtime verification harness. The stale harness was excluded rather than presenting it as a passing test.

## Hardware validation

The Arduino sketch and normalizer show the acquisition contract, but this repository does not provide verified per-device calibration constants or evidence of a complete hardware endurance test.

## Experimental validity

The repository contains no controlled user study, ground-truth motion capture comparison, clinical validation or statistically supported posture-correction result. Scoring and thresholds are prototype rules visible in the source.

## Team attribution

The available code and documents do not establish individual ownership of each class or subsystem. No personal role attribution is made in this portfolio.

## Media and privacy

Original phase captures and report archives were excluded because they may contain identifiable people or private environments. Only media with explicit publication permission should be added.
