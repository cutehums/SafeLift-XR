# Publication Notice

이 저장소는 SafeLift XR 팀 프로젝트의 코드 포트폴리오용 선별본입니다.

## Repository-wide license

팀 차원의 라이선스 합의가 확인되지 않아 저장소 전체에 MIT, Apache-2.0 등의 라이선스를 임의로 적용하지 않았습니다. GitHub 공개 전 팀 구성원과 다음 사항을 합의해야 합니다.

- 공개 가능한 팀 작성 코드의 범위
- 저작권 표시 방식
- 개인별 또는 기관별 권리 관계
- 저장소 전체 라이선스
- 외부 기여 코드가 있다면 원저작자와 라이선스

## Excluded third-party resources

다음 항목은 원본 프로젝트에서 확인됐지만 재배포 권한이 불명확하거나 포트폴리오에 불필요해 포함하지 않았습니다.

- RootMotion Final IK 및 Baker (`licenseType: Store` metadata 확인)
- JetXR UI Kit for Vision Pro OS
- Indoor Gym `.blend` 및 `.fbx`
- Meta XR package cache와 sample assets
- Unity package cache와 sample media
- MediaPipe Action Tracking 외부 integration assets
- XCharts 및 RadialProgressBar source
- system font copies 및 tutorial images
- avatar model, textures, audio와 기타 binary assets

XCharts와 RadialProgressBar 원본에는 MIT license가 있었지만 본 포트폴리오에는 source 자체를 포함하지 않았습니다. JetXR 폴더의 Inter font에는 OFL이 있었으나 UI Kit 전체 라이선스를 의미하지 않으므로 함께 제외했습니다.

## Sanitization applied to this portfolio copy

- 원본 DDNS report base URL을 `https://example.com/reports`로 치환했습니다.
- 원본 사용자 절대경로와 실행용 PowerShell script를 제외했습니다.
- runtime output, report archive, camera captures와 Git history를 제외했습니다.
- 검증 스크립트의 경로를 이 포트폴리오 구조에 맞게 조정했습니다.

원본 ZIP과 원본 Unity 프로젝트는 변경하지 않았습니다.
