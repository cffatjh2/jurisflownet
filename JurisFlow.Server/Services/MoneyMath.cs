namespace JurisFlow.Server.Services
{
    public static class MoneyMath
    {
        public const int DefaultScale = 2;

        public static decimal Normalize(decimal value, int scale = DefaultScale)
            => Math.Round(value, scale, MidpointRounding.AwayFromZero);

        public static decimal ZeroFloor(decimal value, int scale = DefaultScale)
        {
            var normalized = Normalize(value, scale);
            return normalized < 0m ? 0m : normalized;
        }
    }
}
