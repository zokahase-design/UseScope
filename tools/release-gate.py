"""Check explicit release evidence; offline builds alone never authorize publication."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--evidence', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
required = ['WindowsPackageVerified', 'MacArm64PackageVerified', 'MacX64PackageVerified',
            'InteractiveWindowsAccepted', 'InteractiveMacArm64Accepted', 'InteractiveMacX64Accepted',
            'ProviderCoverageAccepted', 'RightsAndTermsAccepted', 'OwnerPublicApproval']
evidence = json.loads(args.evidence.read_text(encoding='utf-8-sig'))
missing = [key for key in required if evidence.get(key) is not True]
result = {'Version': evidence.get('Version'), 'PublicReleaseAllowed': not missing,
          'MissingEvidence': missing, 'Note': 'Package/protocol checks are not live-account or interactive acceptance.'}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, indent=2))
raise SystemExit(0 if not missing else 2)
