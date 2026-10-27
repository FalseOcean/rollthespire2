"""Inventory historical RT2 evidence without touching user caches or running workloads.
Classification is conservative triage; Atlas promotion requires separate source review.
"""
import argparse, collections, datetime, hashlib, json, re
from pathlib import Path

MARKERS = ('physicalExecutionSummary=true', 'gpuCostObservation=true', 'gpuCostPeakDecision=true', 'searchFault=true', 'automaticPrivateSerialStage=true', 'automaticPrivateSerialPricing=true',
 'familyPlannerDecision=true', 'familySearchSummary=true', 'familyAsyncSummary=true',
 'gpuCostPeakSample=true', 'familyPerformanceCalibration=true', 'dispatchSyncMs=',
 'canonicalMs=', 'physicalRevision=', 'runtimeAssembly', 'assemblySha256=',
 'gpuNeow', 'Capability', 'BackendRealizer', 'SearchSession')

def inventory(root):
    rows=[]
    files=list(root.rglob('*'))
    for p in files:
        if not p.is_file() or p.suffix.lower() not in ('.log','.json','.csv','.md','.txt'): continue
        rel=p.relative_to(root).as_posix()
        if not ('rollthespire2' in rel.lower() or rel.startswith('Backup/') or rel.startswith('logs/')): continue
        # Avoid unrelated game/save payloads; only RT2/cache/log evidence is indexed.
        data=p.read_bytes(); text=data.decode('utf-8-sig',errors='replace')
        if not any(x in text.lower() for x in ('rollthespire2','family','beta110','gpu','dispatch','capability')) and not p.name.startswith(('rt2_', 'beta110_')): continue
        counts={m:text.count(m) for m in MARKERS if m in text}
        old=bool(re.search(r'beta110_gpu_|rt2_performance_matrix_p1',p.name)) or ('BackendRealizer' in text)
        stages=counts.get('automaticPrivateSerialStage=true',0)
        classification='B-review' if stages or 'gpuCostObservation=true' in text else 'C' if ('family_local_performance' in p.name or 'physicalRevision=' in text) else 'D'
        if old: classification='D'
        rows.append(dict(file=str(p.resolve()),bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),
            modifiedUtc=datetime.datetime.fromtimestamp(p.stat().st_mtime,datetime.timezone.utc).isoformat(),
            generation='OldFastCandidate' if old else 'Family' if 'Family' in text else 'Unresolved',
            classification=classification,markers=counts,
            header=text.splitlines()[0][:180] if text else '',
            buildEvidence=sorted(set(re.findall(r'(?:rewriteAssemblySha256|assemblySha256|informationalVersion)=[^;\r\n]+',text)))[:5],
            compiledShapes=sorted(set(re.findall(r'([A-Z]\.[^:; ]+):([^:; ]+):input=',text))),
            moduleHashes=sorted(set(re.findall(r'phase=ShaderModuleReady;name=([^;]+);[^\n]*?sourceSha256=([^;]+)',text))),
            revisions=sorted(set(re.findall(r'(?:physicalRevision|physicalImplementationRevision)[=:" ]+([^;\s",]+)',text)))[:40],
            gpuBackendEvidence=sorted(set(re.findall(r'(?:gpu|renderingBackend)=[^;\r\n]+',text)))[:8]))
    return dict(schema=1,scope=str(root),createdUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        enumeratedFiles=sum(p.is_file() for p in files),indexedEvidenceFiles=len(rows),
        classifications=dict(collections.Counter(r['classification'] for r in rows)),files=rows)

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--root',type=Path,required=True);ap.add_argument('--output',type=Path,required=True)
    a=ap.parse_args(); result=inventory(a.root);a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='files'},ensure_ascii=True))
