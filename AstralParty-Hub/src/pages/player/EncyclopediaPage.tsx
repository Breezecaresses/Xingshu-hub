import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Coins, Map, ChevronRight, Sparkles } from 'lucide-react';
import { gameCatalog, plainGameText } from '../../shared/gameLibrary';
import { GameImage, GameText, LibraryHeader, LibraryModal, useGameLibrary } from './LibraryUI';

type LibraryChip = { id: number; name: string; description: string; image: string; quality?: number; qualityName?: string; type?: number; typeName?: string; tags?: (number | string)[]; params?: number[] };
type LibraryMap = { id: number; name: string; description?: string; galleryDescription?: string; image?: string; sceneImage?: string; sceneIds?: number[]; modeIds?: number[]; galleryTypes?: number[]; specialChipIds?: number[]; difficulties?: { index?: number; name?: string; description?: string; turnLimit?: number; monsters?: { id: number; name: string; title: string }[] }[] };
type LibraryMode = { id: number; name: string; description?: string; rules?: string[] | string; mapIds?: number[]; group?: number; visible?: boolean };
type EncyclopediaCatalog = { chips?: LibraryChip[]; maps: LibraryMap[]; modes: LibraryMode[] };
const catalog = gameCatalog as unknown as EncyclopediaCatalog;
const galleryMaps = catalog.maps.filter(map => map.image && map.galleryTypes?.length);
const qualityColors = ['neutral', 'blue', 'purple', 'gold', 'rose', 'cyan'];

export function ChipsPage() {
  const state = useGameLibrary();
  const [query, setQuery] = useState('');
  const [quality, setQuality] = useState('all');
  const [selected, setSelected] = useState<LibraryChip | null>(null);
  const chips = catalog.chips || [];
  const qualities = [...new Set(chips.map(c => c.quality).filter(q => q !== undefined))].sort((a, b) => a! - b!);
  const filtered = useMemo(() => chips.filter(c => `${c.name} ${plainGameText(c.description)} ${c.id}`.toLowerCase().includes(query.toLowerCase()) && (quality === 'all' || c.quality === Number(quality))), [chips, query, quality]);
  const qualityLabel = (chip: LibraryChip) => chip.qualityName || (chip.quality !== undefined ? `品质 ${chip.quality}` : '合作挑战筹码');
  return <div>
    <LibraryHeader title="筹码图鉴" description="合作挑战中的强化选择，查看筹码品质与完整效果" state={state} />
    <div className="encyclopedia-banner"><div className="encyclopedia-banner-icon"><Coins size={30} /></div><div><h2>为每一次冒险做好准备</h2><p>{chips.length ? `${chips.length} 个筹码配置 · 所有效果来自当前游戏版本` : '筹码配置正在准备中'}</p></div><Link to="/player/maps">查看地图 <ChevronRight size={17} /></Link></div>
    <div className="library-filters"><input aria-label="搜索筹码" placeholder="搜索筹码名称或效果…" value={query} onChange={e => setQuery(e.target.value)} /><select aria-label="筹码品质" value={quality} onChange={e => setQuality(e.target.value)}><option value="all">全部品质</option>{qualities.map(q => <option value={q} key={q}>{chips.find(c => c.quality === q)?.qualityName || `品质 ${q}`}</option>)}</select><span>{filtered.length} 个筹码</span></div>
    <div className="chip-grid">{filtered.map(chip => <button className={`chip-card quality-${qualityColors[chip.quality ?? 0] || 'neutral'}`} key={chip.id} onClick={() => setSelected(chip)}>
      <header><span>{qualityLabel(chip)}</span><h3>{chip.name}</h3></header><div className="chip-art"><GameImage asset={chip.image} name={chip.name} /></div><div className="chip-description"><GameText text={chip.description} /><small>#{chip.id}</small></div>
    </button>)}</div>
    {!filtered.length && <div className="library-empty">{chips.length ? '没有符合条件的筹码。' : '尚未导入游戏的筹码配置。'}</div>}
    {selected && <LibraryModal title={selected.name} close={() => setSelected(null)}><div className="chip-detail"><div className={`chip-detail-art quality-${qualityColors[selected.quality ?? 0] || 'neutral'}`}><GameImage asset={selected.image} name={selected.name} /></div><div><span className="library-tag">{qualityLabel(selected)}</span>{selected.typeName && <span className="library-tag">{selected.typeName}</span>}<h3>{selected.name}</h3><GameText text={selected.description} /><p className="card-data-caption library-text">筹码 #{selected.id} · 官方配置 {gameCatalog.version}</p></div></div></LibraryModal>}
  </div>;
}

export function MapsPage() {
  const state = useGameLibrary();
  const [query, setQuery] = useState('');
  const [modeId, setModeId] = useState('all');
  const [selected, setSelected] = useState<LibraryMap | null>(null);
  const [showScene, setShowScene] = useState(false);
  const [tab, setTab] = useState('maps');
  const modes = catalog.modes.filter(m => m.visible !== false && m.name);
  const maps = galleryMaps.filter(m => m.name && `${m.name} ${plainGameText(m.description || '')} ${m.id}`.toLowerCase().includes(query.toLowerCase()) && (modeId === 'all' || m.modeIds?.includes(Number(modeId)) || modes.find(mode => mode.id === Number(modeId))?.mapIds?.includes(m.id)));
  const relatedModes = (map: LibraryMap) => modes.filter(m => map.modeIds?.includes(m.id) || m.mapIds?.includes(map.id));
  const rules = (mode: LibraryMode) => Array.isArray(mode.rules) ? mode.rules.join('\n') : mode.rules;
  const effects = gameCatalog.mutators.filter(m => `${m.name} ${plainGameText(m.description)} ${m.id}`.toLowerCase().includes(query.toLowerCase()));
  return <div>
    <LibraryHeader title="地图与场地" description="地图详情、玩法规则与特殊场地效果" state={state} />
    <div className="encyclopedia-banner map-banner"><div className="encyclopedia-banner-icon"><Map size={30} /></div><div><h2>下一站，去哪里？</h2><p>{galleryMaps.length} 张地图 · {modes.length} 种玩法</p></div><Link to="/player/chips">查看筹码 <ChevronRight size={17} /></Link></div>
    <div className="library-tabs"><button className={`btn ${tab === 'maps' ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setTab('maps')}>地图详情</button><button className={`btn ${tab === 'modes' ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setTab('modes')}>玩法规则</button><button className={`btn ${tab === 'effects' ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setTab('effects')}>特殊场地效果</button></div>
    <div className="library-filters"><input aria-label="搜索地图与效果" placeholder={tab === 'effects' ? '搜索场地效果…' : '搜索地图名称或说明…'} value={query} onChange={e => setQuery(e.target.value)} />{tab === 'maps' && <select aria-label="地图玩法" value={modeId} onChange={e => setModeId(e.target.value)}><option value="all">全部玩法</option>{modes.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select>}</div>
    {tab === 'maps' && <div className="map-grid">{maps.map(map => <button className="map-card" key={map.id} onClick={() => { setSelected(map); setShowScene(false); }}><div className="map-card-art"><GameImage asset={map.image || map.sceneImage || ''} name={map.name} /><span>MAP / {map.id}</span></div><div><h3>{map.name} <ChevronRight size={18} /></h3><div className="map-mode-tags">{relatedModes(map).slice(0, 3).map(mode => <span className="library-tag" key={mode.id}>{mode.name}</span>)}</div><p>{plainGameText(map.galleryDescription || map.description || '查看地图详情与适用玩法')}</p></div></button>)}</div>}
    {tab === 'maps' && !maps.length && <div className="library-empty">没有符合条件的地图。</div>}
    {tab === 'modes' && <div className="mode-grid">{modes.filter(m => `${m.name} ${m.description || ''}`.toLowerCase().includes(query.toLowerCase())).map(mode => <article className="mode-card" key={mode.id}><h3>{mode.name}</h3><GameText text={mode.description} />{rules(mode) && <div className="mode-rules"><GameText text={rules(mode)} /></div>}<div className="mode-map-links">{galleryMaps.filter(map => map.modeIds?.includes(mode.id) || mode.mapIds?.includes(map.id)).map(map => <button className="btn btn-secondary" key={map.id} onClick={() => { setSelected(map); setShowScene(false); }}>{map.name}</button>)}</div></article>)}</div>}
    {tab === 'effects' && <><p className="library-text library-section">以下为游戏中的特殊规则与场地效果，在对应玩法生效。</p><div className="field-effect-grid">{effects.map(effect => <article className="field-effect" key={effect.id}><GameImage asset={effect.image} name={effect.name} /><div><h3><Sparkles size={15} />{effect.name}</h3><GameText text={effect.description} /></div></article>)}</div>{!effects.length && <div className="library-empty">没有符合条件的场地效果。</div>}</>}
    {selected && <LibraryModal title={selected.name} close={() => setSelected(null)}><div className="map-detail"><div className="map-detail-visual"><GameImage asset={(showScene ? selected.sceneImage : selected.image) || selected.sceneImage || selected.image || ''} name={selected.name} />{selected.image && selected.sceneImage && selected.image !== selected.sceneImage && <div className="library-tabs"><button className={`btn ${!showScene ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setShowScene(false)}>地图预览</button><button className={`btn ${showScene ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setShowScene(true)}>场景一览</button></div>}</div><div><span className="library-tag">地图 #{selected.id}</span><h3>{selected.name}</h3><GameText text={selected.galleryDescription || selected.description} />{!!selected.galleryDescription && selected.description && selected.description !== selected.galleryDescription && <div className="library-section"><GameText text={selected.description} /></div>}<div className="map-detail-modes"><h4>适用玩法</h4>{relatedModes(selected).length ? relatedModes(selected).map(mode => <details key={mode.id}><summary>{mode.name}</summary><GameText text={mode.description} />{rules(mode) && <GameText text={rules(mode)} />}</details>) : <p className="library-text">配置暂未提供关联玩法。</p>}</div>{!!selected.difficulties?.length && <div className="map-difficulty-list"><h4>挑战难度</h4>{selected.difficulties.map((difficulty, i) => <details key={difficulty.index ?? i}><summary>{difficulty.name}{!!difficulty.turnLimit && <span>{difficulty.turnLimit} 回合上限</span>}</summary><GameText text={difficulty.description} />{!!difficulty.monsters?.length && <p className="library-text">场地怪物：{difficulty.monsters.map(monster => monster.name || monster.title).join('、')}</p>}</details>)}</div>}{!!selected.specialChipIds?.length && <div className="library-section"><h4>地图专属筹码</h4><div className="map-special-chips">{(catalog.chips || []).filter(chip => selected.specialChipIds?.includes(chip.id)).map(chip => <div key={chip.id}><GameImage asset={chip.image} name={chip.name} /><div><strong>{chip.name}</strong><GameText text={chip.description} /></div></div>)}</div></div>}</div></div></LibraryModal>}
  </div>;
}
