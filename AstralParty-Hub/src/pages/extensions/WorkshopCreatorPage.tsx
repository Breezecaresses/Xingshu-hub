import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, Image as ImageIcon, Paintbrush, Upload } from 'lucide-react';
import {
  formatModBytes,
  installKindLabel,
  submitWorkshopMod,
  type WorkshopCategory,
  type WorkshopSubmitResponse,
} from '../../shared/workshopClient';
import './extensions.css';

export function WorkshopCreatorPage() {
  // Submission form state
  const [subName, setSubName] = useState('');
  const [subAuthor, setSubAuthor] = useState('');
  const [subCategory, setSubCategory] = useState<Exclude<WorkshopCategory, 'all'>>('skin');
  const [subTags, setSubTags] = useState('');
  const [subTargetInfo, setSubTargetInfo] = useState('');
  const [subDescription, setSubDescription] = useState('');
  const [subExternalUrl, setSubExternalUrl] = useState('');
  const [subArchive, setSubArchive] = useState<File | null>(null);
  const [subPreview, setSubPreview] = useState<File | null>(null);
  const [subSubmitting, setSubSubmitting] = useState(false);
  const [subError, setSubError] = useState('');
  const [subResult, setSubResult] = useState<WorkshopSubmitResponse | null>(null);


  const resetSubmissionForm = () => {
    setSubName('');
    setSubAuthor('');
    setSubCategory('skin');
    setSubTags('');
    setSubTargetInfo('');
    setSubDescription('');
    setSubExternalUrl('');
    setSubArchive(null);
    setSubPreview(null);
    setSubError('');
  };

  const handleSubmitMod = async (e: FormEvent) => {
    e.preventDefault();
    setSubError('');
    setSubResult(null);
    setSubSubmitting(true);
    try {
      const res = await submitWorkshopMod({
        name: subName,
        author: subAuthor,
        category: subCategory,
        tags: subTags,
        targetInfo: subTargetInfo,
        description: subDescription,
        externalUrl: subExternalUrl,
        archiveFile: subArchive,
        previewFile: subPreview,
      });
      setSubResult(res);
      resetSubmissionForm();
    } catch (err) {
      setSubError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubSubmitting(false);
    }
  };

  return (
    <div className="workshop-web-page workshop-creator-page">
      <header className="panel workshop-hero">
        <div className="workshop-hero-copy">
          <div className="workshop-eyebrow"><Paintbrush size={15} /><span>CREATOR · 创作与投稿</span></div>
          <h1>创作者入口</h1>
          <p>在这里提交你的 Mod、卡牌替换、角色皮肤和语音特效作品，审核通过后展示在模组工坊。</p>
        </div>
        <Link to="/extensions/mods" className="btn btn-secondary"><ArrowLeft size={14} />返回工坊</Link>
      </header>

      <div className="workshop-creator-grid">
        <section className="panel workshop-creator-editor" aria-labelledby="creator-submit-title">
          <header className="workshop-creator-heading">
            <h2 id="creator-submit-title">投稿作品</h2>
            <p>填写作品信息，上传文件或提供发布链接。</p>
          </header>
          <form onSubmit={handleSubmitMod} className="workshop-form workshop-creator-form">
            {subResult ? (
              <div className="workshop-submit-success">
                <CheckCircle2 size={22} />
                <div>
                  <strong>{subResult.message}</strong>
                  <p>投稿编号：{subResult.id} · 识别类型：{installKindLabel(subResult.installKind)}</p>
                  {subResult.sha256 && <p>SHA-256：<code>{subResult.sha256}</code></p>}
                </div>
              </div>
            ) : (
              <>
                <div className="workshop-form-row">
                  <label>
                    <span>模组名称 *</span>
                    <input
                      type="text"
                      required
                      minLength={2}
                      maxLength={80}
                      value={subName}
                      onChange={(e) => setSubName(e.target.value)}
                      placeholder="例如：芙兰卡高冷立绘替换 / 优化特效卡面"
                    />
                  </label>
                  <label>
                    <span>作者署名 *</span>
                    <input
                      type="text"
                      required
                      minLength={1}
                      maxLength={40}
                      value={subAuthor}
                      onChange={(e) => setSubAuthor(e.target.value)}
                      placeholder="你的昵称或署名"
                    />
                  </label>
                </div>

                <div className="workshop-form-row">
                  <label>
                    <span>模组分类 *</span>
                    <select
                      value={subCategory}
                      onChange={(e) => setSubCategory(e.target.value as Exclude<WorkshopCategory, 'all'>)}
                    >
                      <option value="optimization">优化 Mod</option>
                      <option value="card">卡牌替换</option>
                      <option value="skin">角色皮肤</option>
                      <option value="voice_vfx">语音·特效</option>
                    </select>
                  </label>
                  <label>
                    <span>标签（逗号分隔，最多 8 个）</span>
                    <input
                      type="text"
                      value={subTags}
                      onChange={(e) => setSubTags(e.target.value)}
                      placeholder="角色皮肤, 莉莉, 轻量化"
                    />
                  </label>
                </div>

                <label>
                  <span>替换目标说明（角色、卡牌或 Bundle 名）</span>
                  <input
                    type="text"
                    maxLength={160}
                    value={subTargetInfo}
                    onChange={(e) => setSubTargetInfo(e.target.value)}
                    placeholder="例如：UT_Hero_Bust_103 / 治疗卡卡面优化"
                  />
                </label>

                <label>
                  <span>模组说明 *</span>
                  <textarea
                    rows={3}
                    required
                    minLength={4}
                    maxLength={600}
                    value={subDescription}
                    onChange={(e) => setSubDescription(e.target.value)}
                    placeholder="说明具体修改内容、适配版本（如 3.3.0）及注意事项…"
                  />
                </label>

                <div className="workshop-form-row">
                  <label className="workshop-file-input">
                    <span>上传模组压缩包（.zip / .xspkg，≤ 24MB）</span>
                    <div className="workshop-file-picker">
                      <Upload size={15} />
                      <span>{subArchive ? `${subArchive.name} (${formatModBytes(subArchive.size)})` : '选择本地 .zip 或 .xspkg'}</span>
                      <input
                        type="file"
                        accept=".zip,.xspkg"
                        onChange={(e) => setSubArchive(e.target.files?.[0] || null)}
                      />
                    </div>
                  </label>
                  <label className="workshop-file-input">
                    <span>可选预览图（PNG / JPG / WebP，≤ 2MB）</span>
                    <div className="workshop-file-picker">
                      <ImageIcon size={15} />
                      <span>{subPreview ? `${subPreview.name} (${formatModBytes(subPreview.size)})` : '选择效果截图'}</span>
                      <input
                        type="file"
                        accept="image/png,image/jpeg,image/webp"
                        onChange={(e) => setSubPreview(e.target.files?.[0] || null)}
                      />
                    </div>
                  </label>
                </div>

                <label>
                  <span>或填写安全 HTTPS 外部下载链接（GitHub Release / 网盘外链）</span>
                  <input
                    type="url"
                    value={subExternalUrl}
                    onChange={(e) => setSubExternalUrl(e.target.value)}
                    placeholder="https://..."
                  />
                </label>

                <p className="workshop-detail-note">
                  安全说明：所有投稿压缩包都会检查可执行脚本黑名单、路径穿越，并记录 SHA-256 摘要；管理员审核通过后将对全部玩家开放下载与桌面端一键安装。
                </p>
              </>
            )}

            {subError && <p className="extension-error">{subError}</p>}

            <footer className="workshop-modal-footer">
              <Link
                to="/extensions/mods"
                className="btn btn-secondary"
                aria-disabled={subSubmitting}
                onClick={(event) => { if (subSubmitting) event.preventDefault(); }}
              >
                返回工坊
              </Link>
              {subResult ? (
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => setSubResult(null)}
                >
                  继续投稿
                </button>
              ) : (
                <button type="submit" className="btn btn-primary" disabled={subSubmitting}>
                  <Upload size={14} />
                  {subSubmitting ? '正在校验并上传…' : '提交投稿'}
                </button>
              )}
            </footer>
          </form>
        </section>

        <aside className="panel workshop-creator-guide" aria-label="创作者投稿指南">
          <h2>投稿指南</h2>
          <ol>
            <li><strong>准备作品</strong><p>支持优化 Mod、卡牌替换、角色皮肤与语音特效；请写清适配的游戏版本、渠道和替换目标。</p></li>
            <li><strong>上传或提供链接</strong><p>压缩包支持 .zip / .xspkg，最大 24MB；更大的作品可填写 HTTPS 发布页或网盘链接。预览图最大 2MB。</p></li>
            <li><strong>提交审核</strong><p>提交后保存投稿编号，审核通过的作品会在模组工坊展示。</p></li>
          </ol>
          <div className="workshop-creator-credit">
            <h3>创作与署名</h3>
            <p>请使用原创或已获授权的内容，填写作者署名，并在作品说明中注明引用素材的来源和授权要求。</p>
            <p>作品与游戏素材遵循各自的许可，星枢项目的 MIT 许可不替代作品本身的授权。</p>
          </div>
        </aside>
      </div>
    </div>
  );
}
