using EFT;

namespace pitTeam.Modules
{
    /// <summary>
    /// Projects the finalized SAIN range/scatter baseline into the EFT getters that SAIN
    /// still consumes. SAIN 4.5.1 SetConfigValues does not call its Core.Apply extension.
    /// Keep the role's other core values and every active percentage/temporary modifier.
    /// </summary>
    internal sealed class FollowerSainEftCoreProjection
    {
        private BotSettingsComponents? settings;
        private BotCoreSettings? original;
        private BotCoreSettings? projected;

        internal bool Apply(BotCurrentSettings current, FollowerSainCoreValues values)
        {
            BotSettingsComponents target = current.FileSettings;
            if (!ReferenceEquals(settings, target) || !ReferenceEquals(target.Core, projected))
            {
                Restore();
                settings = target;
                original = target.Core;
                projected = new BotCoreSettings(original);
                target.Core = projected;
            }

            bool visionChanged = projected!.VisibleDistance != values.VisibleDistance;
            projected.VisibleDistance = values.VisibleDistance;
            projected.ScatteringPerMeter = values.ScatteringPerMeter;
            projected.ScatteringClosePerMeter = values.ScatteringClosePerMeter;
            return visionChanged;
        }

        internal void Restore()
        {
            // Do not overwrite a newer settings object installed by another lifecycle owner.
            if (settings != null && ReferenceEquals(settings.Core, projected))
                settings.Core = original;
            settings = null;
            original = null;
            projected = null;
        }
    }
}
