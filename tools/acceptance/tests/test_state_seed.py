"""Configured save source and explicit carry-forward: real files, no game processes."""
import hashlib
import json
from pathlib import Path
import pytest
from acceptance.state_seed import seed_state


def setup(tmp_path, data_path='data/state.json'):
    runtime=tmp_path/'runtime';runtime.mkdir()
    config=runtime/'server.json';config.write_text(json.dumps({'DFLegacy':{'DataPath':data_path}}),encoding='utf-8')
    return config


def write(path, value):
    path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value),encoding='utf-8');return path.read_bytes()


def test_configured_relative_source_is_resolved_against_server_directory(tmp_path):
    config=setup(tmp_path,'custom/edited.json');source=config.parent/'custom/edited.json';raw=write(source,{'Skills':[{'Slot':1,'SkillId':3}]})
    write(config.parent/'data/state.json',{'Skills':[{'Slot':0,'SkillId':3}]})
    dest=tmp_path/'run';record=seed_state(config,dest,environ={})
    assert (dest/'state.json').read_bytes()==raw and source.read_bytes()==raw
    assert record['source']==str(source.resolve()) and record['selection']=='configured'
    assert record['source_sha256']==record['clone_sha256']==hashlib.sha256(raw).hexdigest().upper()


def test_absolute_and_environment_override_follow_server_precedence(tmp_path):
    config=setup(tmp_path);source=tmp_path/'absolute.json';raw=write(source,{'Sp':88})
    record=seed_state(config,tmp_path/'run',environ={'DFLegacy__DataPath':str(source)})
    assert record['selection']=='environment' and (tmp_path/'run/state.json').read_bytes()==raw


def test_explicit_closed_private_save_can_be_carried_forward_without_promoting_edits(tmp_path):
    config=setup(tmp_path);normal=config.parent/'data/state.json';normal_raw=write(normal,{'Slots':[0,1]})
    prior=tmp_path/'closed-private/state.json';prior_raw=write(prior,{'Slots':[1,0]})
    dest=tmp_path/'next-private';record=seed_state(config,dest,seed_state=prior,environ={'DFLegacy__DataPath':'missing.json'})
    assert record['selection']=='explicit' and (dest/'state.json').read_bytes()==prior_raw
    (dest/'state.json').write_text('{"Slots":[3,4]}',encoding='utf-8')
    assert prior.read_bytes()==prior_raw and normal.read_bytes()==normal_raw


@pytest.mark.parametrize('case',['missing','malformed','empty_path','conflicting_override','existing_clone'])
def test_rejects_bad_or_ambiguous_seed_without_overwriting(tmp_path,case):
    config=setup(tmp_path);source=config.parent/'data/state.json';write(source,{'Slots':[0,1]});dest=tmp_path/'run';env={}
    if case=='missing':source.unlink()
    elif case=='malformed':source.write_text('{unfinished',encoding='utf-8')
    elif case=='empty_path':config.write_text('{"DFLegacy":{"DataPath":""}}',encoding='utf-8')
    elif case=='conflicting_override':env={'DFLegacy__DataPath':'a.json','dflegacy__datapath':'b.json'}
    else:write(dest/'state.json',{'sentinel':True})
    with pytest.raises((ValueError,OSError)):seed_state(config,dest,environ=env)
    if case=='existing_clone':assert json.loads((dest/'state.json').read_text())=={'sentinel':True}
    else:assert not (dest/'state.json').exists()


def test_missing_datapath_uses_actual_server_default(tmp_path):
    config=setup(tmp_path);config.write_text('{"DFLegacy":{}}',encoding='utf-8');raw=write(config.parent/'data/state.json',{'Sp':1})
    seed_state(config,tmp_path/'run',environ={});assert (tmp_path/'run/state.json').read_bytes()==raw


def test_colon_form_environment_override_matches_dotnet_provider(tmp_path):
    config=setup(tmp_path);source=tmp_path/'colon.json';raw=write(source,{'Sp':77})
    record=seed_state(config,tmp_path/'run',environ={'dflegacy:datapath':str(source)})
    assert record['selection']=='environment' and (tmp_path/'run/state.json').read_bytes()==raw


def test_conflicting_colon_and_underscore_aliases_are_rejected(tmp_path):
    config=setup(tmp_path);write(config.parent/'data/state.json',{})
    with pytest.raises(ValueError):seed_state(config,tmp_path/'run',environ={
        'DFLegacy:DataPath':'one.json','DFLegacy__DataPath':'two.json'})


def test_agreeing_dotnet_environment_aliases_choose_one_source(tmp_path):
    config=setup(tmp_path);source=tmp_path/'same.json';raw=write(source,{'Sp':4})
    record=seed_state(config,tmp_path/'run',environ={'DFLegacy:DataPath':str(source),'DFLegacy__DataPath':str(source)})
    assert record['selection']=='environment' and (tmp_path/'run/state.json').read_bytes()==raw
