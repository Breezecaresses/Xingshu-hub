import { Link } from 'react-router-dom';
import { gameCatalog, platformName } from '../../shared/gameLibrary';
import { LibraryHeader, LibraryStats, useGameLibrary } from './LibraryUI';

export function ProfilePage() {
  const state = useGameLibrary();
  const a = state.account;
  const data = a?.snapshot;
  return <div><LibraryHeader title="玩家资料" description="当前账号等级、角色收藏与羁绊进度" state={state} />
    {data && a ? <><div className="library-summary"><h2>{data.name || '未命名玩家'}</h2><p>{platformName(a.platform)} · 玩家 ID {data.playerId} · {a.role === 'MAIN' ? '主账号' : a.role === 'USER_SPECTATOR' ? '观战账号' : '未绑定角色'}</p><p>Lv.{data.level} · 当前经验 {data.exp}</p></div>
      <LibraryStats items={[{label:'持有角色',value:data.heroes.length},{label:'羁绊等级合计',value:data.heroes.reduce((n,h)=>n+h.level,0)},{label:'已持有装扮道具',value:data.skins.length},{label:'已持有异画',value:data.altArts.length}]} />
      <div className="library-summary"><h2>收藏进度</h2><p>角色：{data.heroes.filter(h => gameCatalog.characters.some(c=>c.id===h.id)).length} / {gameCatalog.characters.length}</p><p>最高羁绊 Lv.6：{data.heroes.filter(h=>h.level>=6).length} 个角色</p><p>已保存战绩：{data.matches?.length ?? '尚未获取'}{data.matches ? ' 场' : ''}</p></div></> : <div className="library-empty"><h2>等待账号同步</h2><p>启动安装了新版星枢运行时的游戏并登录大厅，选择资料账号后点击“同步游戏数据”。</p><p>角色、皮肤与卡牌图鉴可直接浏览。</p></div>}
    <div className="library-links"><Link to="/player/characters">角色与羁绊<small>查看角色技能、皮肤与等级进度</small></Link><Link to="/player/cards">卡牌与异画<small>完整卡图、分类与详细效果</small></Link><Link to="/player/matches">对战记录<small>按账号保存的真实游戏战绩</small></Link></div>
  </div>;
}
