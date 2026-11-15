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
          <span>Web 下载与投稿 · 桌面端星枢 Hub 支持一键原子安装</span>
        </div>
      </header>
      <WorkshopPanel />
    </div>
  );
};
