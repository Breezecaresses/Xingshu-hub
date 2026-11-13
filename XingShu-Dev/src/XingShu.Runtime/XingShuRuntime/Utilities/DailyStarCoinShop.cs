using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Net;
using Core.Scene;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities;

/// <summary>Opt-in daily shop runner. Uses the native purchase API and never replaces its callbacks.</summary>
internal sealed class DailyStarCoinShop : MonoBehaviour
{
    private static DailyStarCoinShop _instance;
    private const string Prefix = "XINGSHU_DAILY_STARCOIN_";
    private long _uid;
    private bool _online, _enabled, _blocked;
    private string _day = "", _pending = "", _status = "登录游戏后可为此账号开启；默认关闭";
    private float _nextTick, _readyAt, _sentAt;
    private RPCAsyncResult _request;

    internal static bool IsEnabled => _instance != null && _instance._enabled;
    internal static bool CanConfigure => _instance != null && _instance._online && _instance._uid > 0;
    internal static string Status => _instance?._status ?? "等待商店服务初始化";
    public static void Install(GameObject host)
    {
        if (_instance == null) _instance = host.AddComponent<DailyStarCoinShop>();
    }
    private string Key(string suffix) => Prefix + _uid.ToString(CultureInfo.InvariantCulture) + "_" + suffix;
    internal static void SetEnabled(bool enabled)
    {
        var self = _instance;
        if (self == null || !CanConfigure) return;
        self._enabled = enabled;
        // An uncertain purchase is never retried just by toggling the switch.
        self._blocked = false;
        XingShuConfigService.SetBool(self.Key("ENABLED"), enabled);
        XingShuConfigService.Save();
        self._status = enabled ? "已开启此账号的每日星币商店；等待大厅和官方货架" : "已关闭；不再发起新购买";
    }
    private void Update()
    {
        if (Time.realtimeSinceStartup < _nextTick) return;
        _nextTick = Time.realtimeSinceStartup + 1f;
        try { Tick(); }
        catch (Exception ex)
        {
            _blocked = true;
            _status = "本次自动购买已停止：" + ex.GetType().Name + "；请检查官方商店后重新开启";
            Debug.LogWarning("[XingShu DailyShop] Stopped: " + ex.GetType().Name);
        }
    }
    private void Tick()
    {
        var logic = SimpleSingletonProvider<GameLogicManager>.hasInstance ? SimpleSingletonProvider<GameLogicManager>.inst : null;
        bool online = logic?.account != null && logic.account.online &&
            MonoSingletonProvider<NetManager>.hasInstance && MonoSingletonProvider<NetManager>.inst.IsConnected;
        long uid = online ? logic.account.GetPlayerID() : 0;
        if (!online || uid <= 0)
        {
            _online = false;
            _enabled = false;
            _request = null; // Persisted pending marker survives a disconnect.
            _status = "等待游戏账号登录";
            return;
        }
        string day = DailyStarCoinShopPolicy.Day(MonoSingletonProvider<NetManager>.inst.ServerTime);
        if (!_online || uid != _uid)
        {
            _uid = uid;
            _online = true;
            _enabled = XingShuConfigService.GetBool(Key("ENABLED"), false);
            _pending = XingShuConfigService.GetString(Key("PENDING"), "");
            _day = day;
            _blocked = false;
            _request = null;
            _readyAt = Time.realtimeSinceStartup + 5f;
        }
        if (!_enabled) { _status = "此账号未开启每日购买（默认关闭）"; return; }
        if (day != _day)
        {
            // The native store exposes no freshness timestamp. Never use yesterday's
            // stock after 04:00; a fresh official login must initialize it first.
            _status = "商店已跨日，等待下次登录加载今日货架";
            return;
        }
        if (_blocked) return;
        if (Time.realtimeSinceStartup < _readyAt) { _status = "等待官方登录数据就绪"; return; }
        if (!SimpleSingletonProvider<SceneManager>.hasInstance ||
            SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Home ||
            (logic.room?.roomController != null && logic.room.roomController.roomStateType != RoomStateType.NONE) ||
            (logic.match != null && !logic.match.CheckOperateForMatch()) || logic.store == null || logic.bag == null)
        { _status = "已开启；回到大厅且未匹配时自动处理"; return; }
        var ui = SimpleSingletonProvider<UIManager>.hasInstance ? SimpleSingletonProvider<UIManager>.inst : null;
        if (ui == null || (!(ui.currentPanel is HomePanel) && !(ui.currentPanel is ProductRecommendationPanel)) ||
            ShownNativeWindows.Find<PurchaseWindow>(ui) != null)
        { _status = "等待返回大厅或星币商店页面"; return; }

        if (!string.IsNullOrEmpty(_pending))
        {
            string[] p = _pending.Split('|');
            int goods, before, count;
            if (p.Length != 4 || !int.TryParse(p[1], out goods) || !int.TryParse(p[2], out before) ||
                !int.TryParse(p[3], out count) || goods <= 0 || before < 0 || count <= 0)
            { _blocked = true; _status = "购买记录格式异常，已停止自动购买"; return; }
            if (p[0] != day || (long)logic.store.GetGoodsRecord(2, goods) >= (long)before + count)
            {
                ClearPending();
                _request = null;
                _readyAt = Time.realtimeSinceStartup + 1f;
                _status = "上一笔官方限购记录已核对";
                return;
            }
            if (_request != null && !_request.keepWaiting && _request.errId != 0)
            {
                // A transport error can arrive after the server accepted payment.
                // Keep the journal until official stock confirms it, even if toggled.
                _blocked = true;
                _status = "官方购买返回错误 " + _request.errId + "，已暂停；重新登录核对限购后再开启";
                _request = null;
                return;
            }
            _status = (_request != null && Time.realtimeSinceStartup - _sentAt < 20f)
                ? "正在核对官方购买结果…" : "上一笔结果尚未确认，暂停后续购买；请重新登录核对货架";
            return;
        }
        List<DailRefreshGoods> shelf;
        try { shelf = logic.store.GetDailyRefresh(); }
        catch (NullReferenceException) { _status = "等待官方每日货架数据"; return; }
        if (shelf == null || shelf.Count == 0 || shelf.Count > 64) { _status = "今日货架未就绪"; return; }
        var offers = new List<DailyStarCoinShopPolicy.Offer>();
        var ids = new HashSet<int>();
        foreach (var goods in shelf)
        {
            if (goods == null || goods.itemConfig == null || !ids.Add(goods.goodsId))
                throw new InvalidOperationException("Invalid daily shelf");
            offers.Add(new DailyStarCoinShopPolicy.Offer { Id = goods.goodsId, Currency = goods.currencyID,
                Price = goods.SalePrice, Limit = goods.LimitNum(), Purchased = logic.store.GetGoodsRecord(2, goods.goodsId),
                Owned = goods.IsOwn() });
        }
        bool shortBalance;
        var next = DailyStarCoinShopPolicy.Next(offers, logic.bag.GetItemCount(1), out shortBalance);
        if (next == null)
        {
            _status = shortBalance ? "星币不足；已领取免费及可购买商品，余额增加后继续" : "今日星币货架已处理；等待明日登录";
            return;
        }
        // Save before sending. A crash/timeout cannot turn into a duplicate purchase.
        _pending = day + "|" + next.Id + "|" + next.Purchased + "|" + next.Remaining;
        XingShuConfigService.SetString(Key("PENDING"), _pending);
        XingShuConfigService.Save();
        _sentAt = Time.realtimeSinceStartup;
        _request = logic.store.RequestMallBuy(2, next.Id, next.Remaining);
        _status = "已请求官方购买，等待限购记录确认";
    }
    private void ClearPending()
    {
        _pending = "";
        XingShuConfigService.ResetKey(Key("PENDING"));
        XingShuConfigService.Save();
    }
    private void OnDestroy() { if (_instance == this) _instance = null; }
}
