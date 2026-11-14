import { useEffect, useState } from 'react';
import { BookOpen, CheckCircle2, Keyboard, ShieldCheck, Wrench, X } from 'lucide-react';
import { Link } from 'react-router-dom';

type GuideTab = 'usage' | 'install';

interface GuideSection {
  title: string;
  items: string[];
}

const INSTALL_GUIDE: { title: string; subtitle: string; sections: GuideSection[] } = {
  title: '电脑端安装、渠道切换与还原指南',
  subtitle: '覆盖官方安装检查、多渠道定位、文件冲突整理与热更新还原流程',
  sections: [
    {
      title: '一、安装前准备',
      items: [
        '适配《吉星派对》Windows 桌面渠道：Steam 国服、Steam 国际服、TapTap Windows、哔哩哔哩、WeGame（同位置可在「星枢包管理器 / 游戏模组」管理，亦支持在该页连接手机无线调试安装安卓端）。',
        '请先从对应官方启动器打开一次游戏，完成官方资源更新并进入大厅，随后正常退出游戏；安装或更新插件期间不要运行游戏进程。',
        '若 Hub 未自动识别到游戏目录，可在本页点击「探测游戏」，或前往「系统设置 / 星枢包管理器」手动指定实际游戏目录或主程序 exe。',
      ],
    },
    {
      title: '二、检查、备份与安装插件',
      items: [
        '使用安装包内的「星枢安装助手」或 Hub 进行检查与安装：优先选择与当前游戏启动器一致的渠道平台。',
        '同位置若存在其他第三方插件或 MOD 覆盖，助手会在核对资源容器清单后提示备份整理并安装，原文件与校验记录会保存至备份目录。',
        '若提示未知资源、损坏文件或官方版本不匹配，请先通过官方客户端校验/修复游戏资源，进大厅一次并退出后再试，切勿强行覆盖不匹配文件。',
      ],
    },
    {
      title: '三、版本更新与还原官方原版',
      items: [
        'Hub 或安装助手升级后，记得先退出游戏，再用新版安装助手执行一次「安装 / 更新插件」，才会把游戏内运行时更新到最新。',
        '遇到官方热更新替换文件导致 F1 功能中心或悬浮球失效时，请先彻底退出游戏，使用新版插件重装，或点击「还原安装前版本 / 恢复快照」还原本版官方文件。',
        '建议保持重要存档与快照备份；不要跨游戏大版本混用旧版官方还原文件。',
      ],
    },
    {
      title: '四、常见安装与连接问题',
      items: [
        '进入游戏后按 F1 或 `（反引号）无反应：确认从对应渠道官方入口启动游戏、目录探测指向当前正在运行的那份客户端，且未在后台多开错乱的旧路径。',
        'Hub 一直显示「未连接」：保持星枢 Hub 开启，不要被系统安全软件拦截本地回环端口 127.0.0.1:25890；可在「星枢诊断」查看网桥握手与文件校验结果。',
      ],
    },
  ],
};

const USAGE_GUIDE: { title: string; subtitle: string; sections: GuideSection[] } = {
  title: '星枢电脑端与游戏内使用说明',
  subtitle: '功能中心快捷键、手牌监视、自动跟房观战、自动化开关及账号中心指引',
  sections: [
    {
      title: '一、打开星枢功能中心与快捷键',
      items: [
        '保持星枢 Hub 在后台运行，通过官方启动器进入游戏后即会自动挂接本地网桥。',
        'F1：打开 / 收起「星枢功能中心」主面板（包含游戏变速、手牌监视、击飞预测、模组加载、录像回放、自由镜头、自动化与社交、对局数据、高级设置 9 个分页）。',
        '`（Esc 下方反引号键）或点击边缘星形悬浮球：开关悬浮捷径菜单；F7 为游戏变速默认热键，F8 为静态立绘默认热键，可在设置内调整。',
      ],
    },
    {
      title: '二、手牌监视、自动跟房与观战同步',
      items: [
        '进入对局后在功能中心「手牌监视」开启手牌同步；点击左侧角色头像旁的「手牌」可展开/收起牌面，也可开启独立手牌悬浮窗。',
        '本机已同步的牌面会直接显示；真人合作局队友的云端手牌需要已登录对应游戏账号并保持 Hub 运行，由本地网桥与观战同步链路提供真值镜像。',
        '若使用本机观战端/小号双开，请先在「游戏账号」清晰区分并绑定「主账号」与「观战账号」，再前往「星枢观战」按状态引导开启自动跟房。',
      ],
    },
    {
      title: '三、对局统计、镜头与自动化注意项',
      items: [
        '「对局数据」提供击杀、输出、治疗、移动等局内统计与参考评分，结算后由官方战绩补齐（人机对局通常不进入官方历史战绩）。',
        '「自由镜头」支持视野缩放与平移；选目标等原生操作出现时，手牌挂件会自动避让，操作完成后恢复。',
        '「自动化与社交」提供战局守护、自动致谢、局末全赞与星币商店自动购买等功能；其中「自动购买」仅建议在需要清商店时开启，买完后请及时关闭，避免误操作。',
      ],
    },
    {
      title: '四、账号中心、QQ Bot 授权与离线图鉴',
      items: [
        '在「游戏账号」页完成登录后，可用本机渠道服日志票据或粘贴 QQ Bot「趴登录」链接完成一键授权，无需手动在群里发送验证码。',
        '首页与左侧「角色中心 / 卡牌图鉴 / 筹码图鉴 / 地图与场地」来自官方原版配置，无游戏运行也可离线查阅动态异画与完整效果；对战记录按主账号独立留存。',
        '遇到异常请先前往「星枢诊断」导出或查看运行时状态、连接与最近日志，方便快速定位问题。',
      ],
    },
  ],
};

export function DashboardHelpCard() {
  const [activeTab, setActiveTab] = useState<GuideTab | null>(null);

  useEffect(() => {
    if (!activeTab) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setActiveTab(null);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [activeTab]);

  const currentDoc = activeTab === 'install' ? INSTALL_GUIDE : USAGE_GUIDE;

  return (
    <>
      <section className="library-summary xingshu-help-card" aria-label="电脑端使用说明">
        <div className="xingshu-help-header">
          <div>
            <h2>
              <BookOpen size={18} aria-hidden="true" />
              <span>使用说明与快速上手</span>
            </h2>
            <p className="xingshu-help-lead">
              首次使用建议按下方三步完成环境接入；遇热更新替换、无悬浮窗或观战手牌未同步时可随时点开完整说明文档。
            </p>
          </div>
          <div className="xingshu-help-shortcuts" aria-label="游戏内常用热键">
            <span><kbd>F1</kbd> 功能中心</span>
            <span><kbd>~ / `</kbd> 悬浮球</span>
            <span><kbd>F7</kbd> 变速</span>
            <span><kbd>F8</kbd> 静态立绘</span>
          </div>
        </div>

        <div className="xingshu-help-steps">
          <div className="xingshu-help-step">
            <strong>1. 安装与渠道确认</strong>
            <p>先从官方启动器进大厅一次并退出，再用安装助手检查、备份并安装对应渠道插件；保持 Hub 运行后进游。</p>
          </div>
          <div className="xingshu-help-step">
            <strong>2. 进游开启与手牌</strong>
            <p>游戏中按 <code>F1</code> 打开 9 项功能中心；进局在「手牌监视」开启同步，点角色头像旁「手牌」即可查看。</p>
          </div>
          <div className="xingshu-help-step">
            <strong>3. 账号、观战与排障</strong>
            <p>双开/跟房与 Bot 一键授权请前往「游戏账号」；热更新失效或未连接可在「星枢诊断」核查并还原。</p>
          </div>
        </div>

        <div className="library-actions" style={{ marginTop: 14 }}>
          <button type="button" className="btn btn-primary" onClick={() => setActiveTab('usage')}>
            <Keyboard size={15} />
            查看电脑端使用说明
          </button>
          <button type="button" className="btn btn-secondary" onClick={() => setActiveTab('install')}>
            <Wrench size={15} />
            查看安装与还原指南
          </button>
          <Link className="btn btn-secondary" to="/player/accounts">
            <ShieldCheck size={15} />
            前往游戏账号
          </Link>
          <Link className="btn btn-secondary" to="/spectator">
            前往星枢观战
          </Link>
        </div>
      </section>

      {activeTab && (
        <div
          className="xingshu-update-modal-backdrop"
          role="presentation"
          onClick={(e) => {
            if (e.target === e.currentTarget) setActiveTab(null);
          }}
        >
          <div
            className="xingshu-update-modal xingshu-help-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="xingshu-help-modal-title"
          >
            <div className="xingshu-update-modal-top">
              <div className="xingshu-help-modal-tabs" role="tablist" aria-label="说明文档切换">
                <button
                  type="button"
                  role="tab"
                  aria-selected={activeTab === 'usage'}
                  className={`xingshu-help-tab ${activeTab === 'usage' ? 'active' : ''}`}
                  onClick={() => setActiveTab('usage')}
                >
                  <Keyboard size={14} />
                  <span>使用说明与热键</span>
                </button>
                <button
                  type="button"
                  role="tab"
                  aria-selected={activeTab === 'install'}
                  className={`xingshu-help-tab ${activeTab === 'install' ? 'active' : ''}`}
                  onClick={() => setActiveTab('install')}
                >
                  <Wrench size={14} />
                  <span>安装与还原指南</span>
                </button>
              </div>
              <button
                type="button"
                className="xingshu-update-modal-x"
                aria-label="关闭使用说明"
                onClick={() => setActiveTab(null)}
              >
                <X size={18} />
              </button>
            </div>

            <h2 id="xingshu-help-modal-title" className="xingshu-update-modal-title">
              {currentDoc.title}
            </h2>
            <p className="xingshu-update-modal-subtitle">{currentDoc.subtitle}</p>

            <div className="xingshu-help-doc-scroll">
              {currentDoc.sections.map((section) => (
                <section key={section.title} className="xingshu-help-doc-section">
                  <h3>{section.title}</h3>
                  <ul>
                    {section.items.map((item) => (
                      <li key={item}>
                        <CheckCircle2 size={14} aria-hidden="true" />
                        <span>{item}</span>
                      </li>
                    ))}
                  </ul>
                </section>
              ))}
            </div>

            <button
              type="button"
              className="xingshu-update-primary-cta"
              onClick={() => setActiveTab(null)}
            >
              我已了解
            </button>
          </div>
        </div>
      )}
    </>
  );
}
