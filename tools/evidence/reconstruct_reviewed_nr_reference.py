"""Reconstruct the reviewed AppData NR producer window; never imports arbitrary logs.
Outputs raw winning samples/provenance for review. Does not modify the Atlas or cache.
"""
import argparse, hashlib, json, math, re
from pathlib import Path

MODULES = {
 'NeowFamilyPreBones0':'B3DFF65069113B1A2B5640F9CB158DB158FB499CF45CD4EB87977F3B4196BA66',
 'NeowFamilyPreBones1':'CFA146091CDA28032141216586D51A23EB5BBE687CD31CCD120706CE400D8702',
 'NeowFamilyPreBones2':'077B755699299A0EEA99D96192A84C2364A03A81F5E36600C4E707828F797C3D'}
KEY='N.Neow|N.FixedPair28.Identity|N.Neow.Gpu.LocalDonor.DensePreBonesCarry8.CanonicalAbi1Ready.20260905.v2|Dense|PublicInput|PrivateOutput|DispatchSync.FullWindow.20260912.v1'

def reconstruct(path):
    data=path.read_bytes();text=data.decode('utf-8-sig');lines=text.splitlines()
    if 'gpuCostObservation=true;' in text:
        assert 'state=Faulted' not in text and 'searchFault=true' not in text
        modules=dict(re.findall(r'phase=ShaderModuleReady;name=([^;]+);[^\n]*?sourceSha256=([^;]+)',text))
        assert all(modules.get(k)==v for k,v in MODULES.items()), 'current module binding requires review'
        records=[]
        for line in lines:
            if 'gpuCostObservation=true;observationJson=' not in line:continue
            raw=line.split('observationJson=',1)[1].split(';logSession=',1)[0]
            record=json.loads(raw);peak=record.get('bestWindow')
            if record['key']!=KEY or not peak:continue
            assert 'NVIDIA GeForce RTX 4060 Laptop GPU|d3d12' in peak['Device']
            assert peak['SampleActiveMs']>=1000 and peak['SampleInputs']>0
            records.append(peak)
        if not records:raise ValueError('No eligible default window for reviewed key; no raw trace fallback required')
        peak=max(records,key=lambda p:p['PeakThroughput'])
        return dict(classification='A-review',key=KEY,source=str(path.resolve()),sourceSha256=hashlib.sha256(data).hexdigest(),
            moduleSources=MODULES,method='Current default terminal best-window evidence; raw stages not required',
            measurement=dict(inputs=peak['SampleInputs'],activeMs=peak['SampleActiveMs'],throughput=peak['PeakThroughput']))
    assert 'gpu=NVIDIA GeForce RTX 4060 Laptop GPU;renderingBackend=d3d12;' in text
    assert not any(x in text for x in ('familyExecutionFault=true','familyResourceDisposeFailed=true',
        'familyExecutionContextDisposeFailed=true','state=Faulted'))
    assert 'persistentSearchCycleFinished=true;state=Cancelled;scanned=99656663040' in text
    modules={};shapes={};active=False;cold=True;window=[];ms=0;windows=[];observations=0
    for line_no,line in enumerate(lines,1):
        if 'automaticPrivateSerialPricing=true;order=NR;' in line:
            shapes=dict(re.findall(r'([A-Z]\.[^:; ]+):([^:; ]+):input=',line))
        if 'familyPlannerDecision=true;' in line:
            active='AutomaticPrivateSerial.NR.' in line
            cold=True;window=[];ms=0;modules={}
            if active: assert shapes.get('N.Neow')=='N.FixedPair28.Identity'
        if 'phase=ShaderModuleReady;' in line:
            kv=dict(re.findall(r'(\w+)=([^;]+)',line))
            if kv.get('name') in MODULES:modules[kv['name']]=kv['sourceSha256']
        if not active or 'automaticPrivateSerialStage=true;order=NR;stage=N;' not in line:continue
        assert modules==MODULES, 'review required: compiled numerical module changed'
        kv=dict(re.findall(r'(\w+)=([^;]+)',line))
        assert kv['boundary']=='PrivateOrdinalCountReady' and kv['verification']=='False'
        inputs=int(kv['input']);dt=float(kv['dispatchSyncMs'])
        if inputs!=16777216 or not math.isfinite(dt) or dt<=0:continue
        observations+=1
        if cold:cold=False;continue
        window.append(dict(line=line_no,inputs=inputs,activeMs=dt));ms+=dt
        if ms>=1000:
            total=sum(x['inputs'] for x in window)
            windows.append(dict(inputs=total,activeMs=ms,throughput=total*1000/ms,samples=window))
            window=[];ms=0
    assert windows
    return dict(classification='B',key=KEY,source=str(path.resolve()),sourceSha256=hashlib.sha256(data).hexdigest(),
        date='2026-09-12',gpu='NVIDIA GeForce RTX 4060 Laptop GPU',backend='d3d12',moduleSources=MODULES,
        observations=observations,eligibleWindows=len(windows),method='Chronological disjoint >=1000ms dispatch-sync windows; first full batch excluded; per selected session reset; no mixed stage work',
        measurement=max(windows,key=lambda w:w['throughput']))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--log',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    row=reconstruct(a.log);a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps(row,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in row['measurement'].items() if k!='samples'}))
