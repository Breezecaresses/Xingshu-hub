import { isTauriRuntime } from '../../shared/runtime';
import { ExtensionManager } from './ExtensionWorkspace';
import { WorkshopPanel } from './WorkshopPanel';
import './extensions.css';

export const ModsPage = () => {
  if (isTauriRuntime()) {
    return <ExtensionManager view="mods" />;
  }

  return (
    <div className="workshop-web-page">
      <header className="extension-header">
        <div>
          <h1>模组工坊</h1>
          <p>收录并分享星引擎对决玩家社区的优化 Mod、卡牌替换、角色皮肤与语音特效包。</p>
        </div>
        <div className="workshop-mode-badge">
          <span>Web 浏览与下载 · 作品投稿请进入创作者入口</span>
        </div>
      </header>
      <WorkshopPanel />
    </div>
  );
};
