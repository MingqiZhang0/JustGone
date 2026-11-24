namespace JustGone.Core
{
    /// <summary>
    /// Decision when GameManager becomes invalid during an active custom session.
    /// Directly reused from WeiXiangQi v0.10.10.0 (shared_p4/WxqCompatGuards.cs WxqSessionGuard).
    /// </summary>
    public enum SessionGuardDecision
    {
        KeepRunning = 0,
        WaitForSceneChange = 1,
        TeardownNormal = 2,
        AbortFailsafe = 3
    }

    /// <summary>
    /// Pure-logic guard distinguishing normal scene unload from abnormal GameManager destruction.
    /// </summary>
    public static class SessionGuard
    {
        public const float DefaultGraceSeconds = 1.5f;

        public static SessionGuardDecision Decide(
            bool gameManagerAlive,
            bool sessionSceneIsExpected,
            bool expectedSceneLoaded,
            float secondsSinceGameManagerLost,
            float graceSeconds = DefaultGraceSeconds)
        {
            if (gameManagerAlive) return SessionGuardDecision.KeepRunning;

            if (!sessionSceneIsExpected || !expectedSceneLoaded)
                return SessionGuardDecision.TeardownNormal;

            if (secondsSinceGameManagerLost < graceSeconds)
                return SessionGuardDecision.WaitForSceneChange;

            return SessionGuardDecision.AbortFailsafe;
        }

        public static string Describe(SessionGuardDecision decision)
        {
            switch (decision)
            {
                case SessionGuardDecision.KeepRunning: return "keep-running";
                case SessionGuardDecision.WaitForSceneChange: return "wait-scene-change";
                case SessionGuardDecision.TeardownNormal: return "teardown-normal";
                default: return "abort-failsafe";
            }
        }
    }
}
