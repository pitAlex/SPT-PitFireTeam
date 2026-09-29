namespace pitTeam.Server.Constants;

/// <summary>
/// Centralized EFT item template ids used by pitFireTeam server logic.
/// If an SPT/EFT version changes templates, update this file before chasing usages.
/// </summary>
internal static class FriendlyItemTemplateIds
{
    internal static class Currency
    {
        public const string Roubles = "5449016a4bdc2d6f028b456f";
    }

    internal static class EquipmentContainer
    {
        public const string Pockets = "627a4e6b255f7527fb05a0f6";
    }

    internal static class SecureContainer
    {
        public const string Beta = "5857a8b324597729ab0a0e7d";
        public const string Epsilon = "59db794186f77448bc595262";
        public const string Gamma = "5857a8bc2459772bad15db29";
        public const string Boss = "5c0a794586f77461c458f892";
    }

    internal static class Weapon
    {
        public const string DefaultKnife = "54491bb74bdc2d09088b4567";
        public const string HiringBayonet = "5bffdc370db834001d23eca8";
    }

    internal static class Medical
    {
        public const string GrizzlyMedicalKit = "590c657e86f77412b013051d";
        public const string Surv12SurgicalKit = "5d02797c86f774203f38e30a";
        public const string CmsSurgicalKit = "60d4399358ef941a33423dad";
        public const string SalewaFirstAidKit = "544fb45d4bdc2dee738b4568";
        public const string Ai2Medkit = "544fc38949f06fd411383b42";
        public const string CarFirstAidKit = "5c0e30fa86f77413531e1cd3";
        public const string AnalginPainkillers = "5e831507ea0a7c419314e497";
        public const string MorphineInjector = "5e8488fa988873513c331205";
        public const string ArmyBandage = "544fb37d4bdc2dee738b4567";
        public const string RegularBandage = "544fb44d4bdc2dee738b4568";
    }
}
