import { useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight, Heart, Sparkles, Sword, Shield, Users } from 'lucide-react';
import { gameCatalog, libraryCharacterName, isPreviewCharacter } from '../../shared/gameLibrary';
import type { HeroProgress, LibraryCharacter, LibrarySkin } from '../../shared/gameLibrary';
import { GameImage, GameText, LibraryHeader, RewardItems, useGameLibrary } from './LibraryUI';

type CharacterDetails = LibraryCharacter & {
  pveProgression?: { level: number; description: string }[];
  pvePotential?: { id?: number; description?: string; skills?: { id: number; name: string; description: string }[]; requiredLevel?: number; materials?: Record<string, number> } | null;
  emotes?: { id: number; name: string; image: string }[];
};
type CharacterProgress = HeroProgress & { pveLevel?: number | null; pveExp?: number | null; potential?: number[] };
const characters = gameCatalog.characters as CharacterDetails[];
const releasedCharacters = characters.filter(c => !isPreviewCharacter(c));

export function CharactersPage() {
  const state = useGameLibrary();
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState('all');
  const [selectedId, setSelectedId] = useState(characters[0]?.id);
  const [skinId, setSkinId] = useState<number | null>(null);
  const [tab, setTab] = useState('roster');
  const [skillMode, setSkillMode] = useState('pvp');
  const progress = state.account?.snapshot.heroes as CharacterProgress[] | undefined;
  const detail = characters.find(c => c.id === selectedId) || characters[0];
  const preview = !!detail && isPreviewCharacter(detail);
  const p = progress?.find(h => h.id === detail?.id);
  const visible = useMemo(() => characters.filter(c => `${libraryCharacterName(c)} ${c.title} ${c.id}`.toLowerCase().includes(query.toLowerCase()) && (filter !== 'owned' || progress?.some(h => h.id === c.id)) && (filter !== 'bond' || c.hasBond) && (filter !== 'preview' || isPreviewCharacter(c))), [query, filter, progress]);
  const currentSkin = detail?.skins.find(s => s.id === skinId) || detail?.skins.find(s => s.index === p?.skin) || detail?.skins.find(s => s.default) || detail?.skins[0];
  const maxBond = Math.max(...gameCatalog.favorLevels.map(l => l.Id || 0), 0);
  const knownOwned = progress?.filter(h => characters.some(c => c.id === h.id)).length;
  const ownedSkin = (s: LibrarySkin) => !state.account ? '持有未知' : state.account.snapshot.skins.includes(s.id) || (!!p && (s.default || p.skin === s.index)) ? '已持有' : '未持有';
  const selectCharacter = (id: number) => { setSelectedId(id); setSkinId(null); };
  const stepCharacter = (direction: number) => {
    const index = characters.findIndex(c => c.id === detail?.id);
    selectCharacter(characters[(index + direction + characters.length) % characters.length].id);
  };
  const tabs = [['roster', '角色'], ['skills', '技能'], ['growth', '成长与潜能'], ['bond', '羁绊'], ['skins', '皮肤'], ['archive', '档案']];
  if (detail?.emotes?.length) tabs.push(['emotes', '表情']);
  if (detail?.preview) tabs.push(['preview', '预装配置']);
  if (!detail) return <div className="library-empty">角色配置尚未准备好。</div>;
  return <div>
    <LibraryHeader title="角色中心" description="走近每位角色，查看技能、成长、皮肤与羁绊" state={state} />
    <div className="character-collection-line"><span><Users size={15} /> 角色图鉴 <strong>{characters.length}</strong></span><span>官方预更新 <strong>{characters.filter(isPreviewCharacter).length}</strong></span><span>已持有 <strong>{knownOwned ?? '待同步'}</strong>{knownOwned !== undefined && ` / ${releasedCharacters.length}`}</span><span>满羁绊 <strong>{progress ? progress.filter(h => characters.some(c => c.id === h.id) && h.level >= maxBond).length : '待同步'}</strong></span><span>皮肤与装扮 <strong>{characters.reduce((n, c) => n + c.skins.length, 0)}</strong></span></div>
    {preview && <div className="character-preview-notice">官方预更新角色 · 已收录当前预装数值、技能参数、关联状态与成长。名称和完整效果尚未提供，借用其他角色的名称或形象已收起；内容可能在正式上线前调整。</div>}
    <div className="character-studio">
      <aside className="character-poster">
        <div className="character-poster-halo" />
        <div className="character-poster-heading"><span>CHARACTER / {String(detail.id).padStart(3, '0')}</span><span className="library-tag">{preview ? '官方预更新' : !progress ? '持有未知' : p ? '已持有' : '未持有'}</span></div>
        <GameImage key={`${detail.id}:${currentSkin?.id}`} asset={currentSkin?.image || detail.image} name={currentSkin?.name || libraryCharacterName(detail)} className="character-full-art" />
        <div className="character-poster-caption"><p>{detail.title}</p><h2>{libraryCharacterName(detail)}</h2><p>{currentSkin?.name}</p><div className="character-bond-hearts" aria-label={p ? `羁绊等级 ${p.level}` : '羁绊进度未知'}>{Array.from({ length: Math.min(maxBond, 6) }, (_, i) => <Heart key={i} size={22} fill={p && p.level > i ? 'currentColor' : 'none'} className={p && p.level > i ? 'filled' : ''} />)}</div><span>{p ? `羁绊 Lv.${p.level}${p.level >= maxBond ? ' · MAX' : ''}` : preview ? '资料尚未完整发布' : '同步后查看羁绊进度'}</span></div>
        <button className="character-step previous" aria-label="上一个角色" onClick={() => stepCharacter(-1)}><ChevronLeft size={22} /></button><button className="character-step next" aria-label="下一个角色" onClick={() => stepCharacter(1)}><ChevronRight size={22} /></button>
      </aside>
      <section className="character-workspace">
        <div className="character-tabs" role="tablist" aria-label="角色资料">{tabs.map(([key, label]) => <button key={key} role="tab" aria-selected={tab === key} className={tab === key ? 'active' : ''} onClick={() => setTab(key)}>{label}</button>)}</div>
        <div className="character-workspace-body">
          {tab === 'roster' && <>
            <div className="library-filters"><input aria-label="搜索角色" placeholder="搜索角色、称号或编号…" value={query} onChange={e => setQuery(e.target.value)} /><select aria-label="角色筛选" value={filter} onChange={e => setFilter(e.target.value)}><option value="all">全部角色</option><option value="owned">我持有的角色</option><option value="bond">支持羁绊</option><option value="preview">官方预更新</option></select></div>
            <div className="character-roster">{visible.map(c => {
              const hero = progress?.find(h => h.id === c.id);
              return <button className={`character-roster-card ${detail.id === c.id ? 'selected' : ''}`} key={c.id} onClick={() => selectCharacter(c.id)} aria-pressed={detail.id === c.id}>
                <GameImage asset={c.skins.find(s => s.default)?.image || c.image} name={libraryCharacterName(c)} />
                <div><strong>{libraryCharacterName(c)}</strong><small>{isPreviewCharacter(c) ? '官方预更新 · 资料待补全' : !progress ? '持有未知' : hero ? `羁绊 Lv.${hero.level}` : '未持有'}</small></div>
              </button>;
            })}</div>
            {!visible.length && <div className="library-empty">{filter === 'owned' && !progress ? '同步账号后可筛选已持有角色。' : '没有符合条件的角色。'}</div>}
            <div className="character-roster-footer"><span>{visible.length} 个角色</span><button className="btn btn-primary" onClick={() => setTab('skills')}>查看 {libraryCharacterName(detail)} 的资料 <ChevronRight size={15} /></button></div>
          </>}
          {tab !== 'roster' && <div className="character-detail-heading"><div><span>{detail.title}</span><h2>{libraryCharacterName(detail)}</h2></div><button className="btn btn-secondary" onClick={() => setTab('roster')}>切换角色</button></div>}
          {tab === 'skills' && <>
            <div className="character-attributes"><span><Sword size={17} /> 攻击 <strong>{detail.attack}</strong></span><span><Shield size={17} /> 防御 <strong>{detail.defense}</strong></span><span><Heart size={17} /> 生命 <strong>{detail.hp}</strong></span></div>
            {preview && <p className="library-text library-section">定位：{detail.category || '待更新'}{detail.isLinkage ? ' · 配置标记为联动角色' : ''}。当前仅有预装数据，不能据此确认正式身份。</p>}
            <div className="library-tabs"><button className={`btn ${skillMode === 'pvp' ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setSkillMode('pvp')}>标准模式 · PVP</button><button className={`btn ${skillMode === 'pve' ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setSkillMode('pve')}>合作挑战 · PVE</button></div>
            <div className="character-skill-list">{(skillMode === 'pvp' ? detail.skills : detail.pveSkills).map((skill, i) => <article key={skill.id} className="character-skill"><span className="character-skill-number">{String(i + 1).padStart(2, '0')}</span><div><div className="character-skill-title"><h3>{skill.name || `${skill.kind === 'active' ? '主动' : '被动'}技能 #${skill.id}`}</h3>{skill.cooldown > 0 && <span className="library-tag">冷却 {skill.cooldown} 回合{preview ? ' · 暂定' : ''}</span>}</div><GameText text={skill.description || '官方尚未填写技能效果，等待后续配置更新。'} />{preview && (Boolean(skill.params?.length) || Boolean(skill.buffs?.length)) && <details className="character-config-details"><summary>预装参数与关联状态</summary><p className="library-text">参数：{skill.params?.length ? skill.params.join(' / ') : '未设置'}。参数含义尚未完整提供，不按顺序推测伤害或效果。</p>{skill.buffs?.map(buff => <div className="character-config-state" key={buff.id}><strong>{buff.name || `状态 #${buff.id}`}</strong><GameText text={buff.description || '状态文案待更新。'} />{buff.clearedOnDeath && <span className="library-tag">死亡清除</span>}</div>)}{Boolean(skill.relatedCharacterIds?.length) && <p className="library-text">参数中引用角色编号：{skill.relatedCharacterIds?.map(id => `#${id}`).join('、')}，具体关联效果待更新。</p>}</details>}</div></article>)}</div>
            {!(skillMode === 'pvp' ? detail.skills : detail.pveSkills).length && <div className="library-empty">官方配置暂未提供此模式的技能说明。</div>}
          </>}
          {tab === 'growth' && <>
            {preview && <p className="character-preview-notice">以下为预装的成长描述，其中包含共享模板；只展示已提供的原文，尚未验证实战效果。</p>}
            <div className="character-progress-summary"><Sparkles size={24} /><div><strong>合作挑战成长</strong><p>{typeof p?.pveLevel === 'number' ? `当前 PVE 等级 ${p.pveLevel}${typeof p.pveExp === 'number' ? ` · 经验 ${p.pveExp}` : ''}` : '角色成长等级与羁绊等级分别记录；当前尚未同步 PVE 等级。'}</p></div></div>
            <div className="character-upgrades">{detail.pveProgression?.map(level => <div key={level.level} className={`character-upgrade ${typeof p?.pveLevel === 'number' && p.pveLevel >= level.level ? 'reached' : ''}`}><strong>Lv.{level.level}</strong><GameText text={level.description} />{typeof p?.pveLevel === 'number' && p.pveLevel >= level.level && <span>已达到</span>}</div>)}</div>
            {!detail.pveProgression?.length && <div className="library-empty">官方配置暂未提供此角色的 PVE 等级效果。</div>}
            <div className="character-potential"><div><Sparkles size={20} /><h3>潜能激发</h3>{detail.pvePotential?.requiredLevel !== undefined && <span className="library-tag">所需 PVE 等级 {detail.pvePotential.requiredLevel}</span>}</div>{detail.pvePotential?.description && <GameText text={detail.pvePotential.description} />}{detail.pvePotential?.skills?.map(s => <div key={s.id}><h4>{s.name}</h4><GameText text={s.description} /></div>)}{!detail.pvePotential?.description && !detail.pvePotential?.skills?.length && <p className="library-text">官方配置暂未提供潜能说明。</p>}{detail.pvePotential?.materials && <div className="library-section"><h4>激发材料</h4><p className="library-text"><RewardItems items={detail.pvePotential.materials} /></p></div>}{p?.potential && detail.pvePotential?.id && <span className="library-tag">{p.potential.includes(detail.pvePotential.id) ? '已激发' : '未激发'}</span>}</div>
          </>}
          {tab === 'bond' && <>
            <div className="character-progress-summary"><Heart size={24} /><div><strong>{p ? `羁绊 Lv.${p.level}${p.level >= maxBond ? ' · MAX' : ''}` : '羁绊进度待同步'}</strong><p>{p ? `经验 ${p.exp} · ${p.breakthrough ? '已完成羁绊突破' : '尚未完成羁绊突破'}` : '以下为游戏中的羁绊等级与奖励，账号进度在同步后显示。'}</p></div></div>
            {detail.hasBond ? gameCatalog.favorLevels.filter(l => (l.Id || 0) > 0).map(l => <div className={`character-bond-level ${p && p.level >= (l.Id || 0) ? 'reached' : ''}`} key={l.Id || 0}><strong><Heart size={18} /> {l.Id}</strong><div><h3>友情等级 {l.Id}</h3><p><RewardItems items={detail.bondRewards.find(r => r.FavorLevel === l.Id)?.Rewards} /></p><small>{l.NeedExp ? `升至下一级所需经验 ${l.NeedExp}` : '最高羁绊等级'}</small></div>{p && p.level >= (l.Id || 0) && <span className="library-tag">已达到</span>}</div>) : <div className="library-empty">此角色暂无羁绊配置。</div>}
            {detail.hasBond && detail.breakthrough.NeedMaterials && <div className="library-section"><h3>羁绊突破材料</h3><p className="library-text"><RewardItems items={detail.breakthrough.NeedMaterials} /></p></div>}
            {detail.hasBond && detail.breakthrough.Rewards && <div className="library-section"><h3>突破奖励</h3><p className="library-text"><RewardItems items={detail.breakthrough.Rewards} /></p></div>}
          </>}
          {tab === 'skins' && <>
            {preview && <p className="character-preview-notice">装扮栏位已预装，当前资源未提供对应立绘；占位名称不代表正式皮肤名称。</p>}
            <p className="library-text library-section">选择皮肤可在左侧查看完整立绘。{currentSkin && `当前预览：${currentSkin.name}。`}</p>
            <div className="character-skins">{detail.skins.map(s => <button key={s.id} className={`character-skin-card ${currentSkin?.id === s.id ? 'selected' : ''}`} onClick={() => setSkinId(s.id)} aria-pressed={currentSkin?.id === s.id}><GameImage asset={s.image} name={s.name} /><div><h3>{s.name}</h3><p>{ownedSkin(s)}{p?.skin === s.index ? ' · 游戏当前装扮' : ''}</p></div></button>)}</div>
            {currentSkin?.description && <div className="character-skin-caption"><h3>{currentSkin.name}</h3><GameText text={currentSkin.description} /></div>}
          </>}
          {tab === 'archive' && <><div className="character-archive"><h3>{detail.title}</h3><GameText text={detail.biography || detail.comment || '官方尚未填写角色档案。'} /></div>{detail.comment && detail.biography && <div className="library-section"><GameText text={detail.comment} /></div>}</>}
          {tab === 'emotes' && <>{preview && <p className="character-preview-notice">已声明 {detail.preview?.emoteSlots} 个表情栏位，图片与正式名称待更新。</p>}<div className="character-emotes">{detail.emotes?.map(emote => <article key={emote.id}><GameImage asset={emote.image} name={emote.name} /><p>{emote.name}</p></article>)}</div></>}
          {tab === 'preview' && detail.preview && <div className="character-config-review"><h3>已预装与待补全</h3><p className="library-text">{detail.skills.length} 条标准技能 · {detail.pveSkills.length} 条合作技能 · {detail.pveProgression?.length || 0} 级成长 · {detail.preview.skinSlots} 个装扮栏位 · {detail.preview.emoteSlots} 个表情栏位</p><p className="library-text">待补全：{detail.preview.missing.join('、') || '无'}。</p><h3>已隔离的占位引用</h3><p className="library-text">以下内容来自原始配置，保留作核对依据，不作为角色的正式身份或奖励。</p>{detail.preview.placeholderReferences.map((ref, i) => <article key={`${ref.field}:${i}`}><strong>{ref.field}</strong><code>{ref.value}</code><p className="library-text">{ref.reason}</p></article>)}<details className="character-config-details"><summary>查看缺失文案与图片编号</summary><p className="library-text">{detail.preview.missingTextIds.map(r => `${r.table}:${r.id}`).join('、')}</p><p className="library-text">{detail.preview.unavailableAssets.join('、')}</p></details></div>}
        </div>
      </section>
    </div>
  </div>;
}
