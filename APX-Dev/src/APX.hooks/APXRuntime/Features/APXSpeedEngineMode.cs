namespace APXRuntime.Features
{
    /// <summary>
    /// 游戏全局变速引擎模式 (Phase 2D-2B2)
    /// 内部枚举，定义两种严格互斥的加速引擎
    /// </summary>
    internal enum APXSpeedEngineMode
    {
        /// <summary>
        /// 0: 游戏内托管加速 (Unity Time.timeScale / DOTween / Animator / ParticleSystem)
        /// </summary>
        ManagedAPX = 0,

        /// <summary>
        /// 1: 原生系统时间加速 (Windows QPC / GetTickCount64 / timeGetTime Detours)
        /// </summary>
        NativeHook = 1
    }
}
