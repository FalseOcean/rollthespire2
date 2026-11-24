#!/usr/bin/env python3
from pathlib import Path
import json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]

def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

preset = read('Ui/Persistence/SearchPresetPersistence.cs')
csproj = read('RolltheSpire2.csproj')
overlay = read('Ui/Components/SearchPresetLibraryOverlay.cs')
provider = preset[preset.index('internal sealed class BuiltInSearchPresetProvider'):preset.index('internal sealed class UserSearchPresetProvider')]

# Bundled-only source/content contract.
req('officialPresetFolderExists', (ROOT / 'OfficialPresets').is_dir())
files = sorted((ROOT / 'OfficialPresets').glob('*.json'))
req('ownerProvidedInitialOfficialPresetsPresent', len(files) >= 3)
req('csprojEmbedsWildcard', 'EmbeddedResource Include="OfficialPresets\\*.json"' in csproj)
req('stableLogicalResourcePrefix', 'RolltheSpire2.OfficialPresets.%(Filename)%(Extension)' in csproj)
req('providerEnumeratesEmbeddedResources', 'assembly.GetManifestResourceNames()' in provider and 'GetManifestResourceStream' in provider)
req('providerNoNetwork', all(token not in provider for token in ['HttpClient', 'WebRequest', 'Steam API', 'https://', 'http://']))
req('providerNoRuntimeExternalDirectoryScan', 'Directory.EnumerateFiles' not in provider and 'File.ReadAllText' not in provider)

# One ordinary user-exported JSON can be dropped in without manual Id editing.
req('officialIdentityPrefix', 'OfficialIdPrefix = "official:"' in provider)
req('filenameOwnsOfficialIdentity', 'persisted.Id = BuildOfficialId(resourceName);' in provider)
req('fileStemBuildsStableId', 'Path.GetFileNameWithoutExtension(fileName)' in provider)
req('builtInNormalizeUsesOfficialPrefix', 'SearchPresetSource.BuiltIn => BuiltInSearchPresetProvider.OfficialIdPrefix' in preset)
req('builtInSourceUsesSameNormalizer', 'SearchPresetStore.Normalize(' in provider and 'SearchPresetSource.BuiltIn' in provider)
req('samePersistedDto', 'JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>' in provider)
req('sameCatalogLoadPath', '_builtIn.GetPresets()' in preset and '_temporary.GetPresets()' in preset and '_user.GetPresets()' in preset)
req('noHardcodedPresetConstruction', 'new SearchPresetDefinition(' not in provider)

# Fail-soft per bundled entry.
for token in ['officialSearchPresetEntrySkipped=true', 'reason=FutureSchema', 'reason=DuplicateId', 'reason=DuplicateName', 'catch (Exception ex)']:
    req('failSoft_' + token.replace('=', '_').replace(' ', '_').replace('(', '').replace(')', ''), token in provider)
req('malformedEntryCannotBlockOtherEntries', 'foreach (string resourceName in resourceNames)' in provider and 'presets.Add(preset);' in provider)

# Official entries remain read-only in the current Preset Library product surface.
req('officialNotDeleteable', '_delete.Visible = valid && preset!.Source == SearchPresetSource.User' in overlay)
req('officialNotEditable', '_editInfo.Visible = valid && preset!.Source == SearchPresetSource.User' in overlay)
req('officialNotShareAsUser', '_share.Visible = valid && preset!.Source == SearchPresetSource.User' in overlay)
req('officialSectionExists', 'AddSection(SearchPresetSource.BuiltIn' in overlay)

# Validate every bundled JSON against the existing v2 asset envelope. The original
# user:* Id is intentionally allowed because the provider replaces it from filename.
titles = set(); stems = set()
for path in files:
    data = json.loads(path.read_text(encoding='utf-8'))
    stem = path.stem
    req(f'{stem}_schemaSupported', isinstance(data.get('SchemaVersion'), int) and data['SchemaVersion'] <= 2)
    req(f'{stem}_userExportAccepted', isinstance(data.get('Id'), str) and len(data['Id'].strip()) > 0)
    req(f'{stem}_title', isinstance(data.get('Title'), str) and bool(data['Title'].strip()))
    req(f'{stem}_character', isinstance(data.get('CharacterKey'), str) and data['CharacterKey'].startswith('CHARACTER:'))
    req(f'{stem}_querySnapshot', isinstance(data.get('QuerySnapshot'), dict))
    req(f'{stem}_conditionCount', isinstance(data.get('ConditionCount'), int) and data['ConditionCount'] >= 0)
    req(f'{stem}_visualIcons', isinstance(data.get('VisualIcons'), list) and len(data['VisualIcons']) <= 3)
    req(f'{stem}_officialIdUnique', stem not in stems)
    req(f'{stem}_titleUnique', data['Title'].casefold() not in titles)
    stems.add(stem); titles.add(data['Title'].casefold())
    req(f'{stem}_titleNotHardcodedInCSharp', data['Title'] not in provider)

# Owner-provided initial three assets are present without rewriting their filenames.
for stem in ['adf3341753694172bc3d17da44bae42b','71d23ef131d143359197887d3545cc16','532a1da801db450a8f3e39600d5ac609']:
    req('initialAsset_' + stem, (ROOT / 'OfficialPresets' / (stem + '.json')).is_file())

# Project file still parses after adding the wildcard resource.
ET.fromstring(csproj)
req('csprojXmlValid', True)
print(f'officialPresetBundledContentCount={len(files)}')
print('officialPresetBundledContentGate=true')
