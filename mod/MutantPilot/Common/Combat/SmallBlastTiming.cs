using System;

namespace MutantPilot.Combat
{
    public static class SmallBlastTiming
    {
        public static bool Damages(int age)
        {
            if (age < 0 || age >= 21) return false;
            int frame = age / 3;
            return frame <= 2 || frame >= 5;
        }
        // Virtual births can differ by one projectile update. Keep the union
        // of these timings; the middle four ticks remain a real safe gap.
        public static bool CouldDamage(int age, int uncertainty = 1)
        {
            for (int shift = -uncertainty; shift <= uncertainty; shift++)
                if (Damages(age + shift)) return true;
            return false;
        }
    }
}
