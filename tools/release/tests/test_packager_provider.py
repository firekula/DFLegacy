"""Exercise the real offline WinForms request when selecting each source."""
import os
from pathlib import Path
import subprocess

import pytest


@pytest.mark.skipif(os.name != 'nt', reason='Windows Forms packager')
def test_provider_selection_defaults_to_gitea_and_preserves_other_providers(tmp_path):
    project=Path(__file__).parent/'packager-probe'/'PackagerProbe.csproj'
    result=subprocess.run(['dotnet','run','--project',str(project),'--configuration','Release',
                           '--artifacts-path',str(tmp_path/'artifacts')],capture_output=True,text=True,timeout=180)
    assert result.returncode==0, result.stdout+result.stderr
    assert 'source request verified' in result.stdout
