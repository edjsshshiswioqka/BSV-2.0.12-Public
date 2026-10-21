namespace BetterStardewValley.Orders;
internal readonly record struct OrderRect(int X,int Y,int W,int H)
{internal bool Contains(int x,int y)=>x>=X&&x<X+W&&y>=Y&&y<Y+H;}
// A — one geometry model for drawing, hit testing and headless viewport checks.
internal sealed class OrdersLayout
{
 internal const int Width=1040,Height=680;
 internal readonly float Scale;internal readonly int X,Y;
 internal OrdersLayout(int width,int height){Scale=Math.Min(1f,Math.Min((width-24f)/Width,(height-24f)/Height));Scale=Math.Max(.1f,Scale);X=(int)((width-Width*Scale)/2);Y=(int)((height-Height*Scale)/2);}
 internal OrderRect Screen(OrderRect r)=>new(X+(int)(r.X*Scale),Y+(int)(r.Y*Scale),(int)(r.W*Scale),(int)(r.H*Scale));
 internal OrderRect Tab(int n)=>new(30+n*326,83,320,48);
 internal OrderRect Row(int n)=>new(30,157+n*101,414,94);
 internal readonly OrderRect Previous=new(32,574,60,32),Next=new(383,574,60,32),Action=new(477,554,164,51),Reject=new(651,554,187,51),Close=new(848,554,159,51);
}
