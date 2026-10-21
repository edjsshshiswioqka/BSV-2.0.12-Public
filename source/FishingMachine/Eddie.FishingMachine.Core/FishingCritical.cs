namespace Eddie.FishingMachine.Core;

public static class FishingCritical
{
    // Separate random stream keeps base outcomes/species independent of the bonus setting.
    public static Random RandomFor(int seed)=>new(unchecked(seed ^ 0x5EEDC17));
    public static int Bonus(double probability,double roll)
    {
        if(!double.IsFinite(probability) || probability<0 || probability>1
            || !double.IsFinite(roll) || roll<0 || roll>=1)throw new ArgumentOutOfRangeException();
        return roll<probability?1:0;
    }
}
