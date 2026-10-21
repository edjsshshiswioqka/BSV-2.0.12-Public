using System.Globalization;

namespace BetterStardewValley;

// Display-only helpers. Measure with the actual active font; never split a
// surrogate pair or combining sequence when wrapping or shortening UI text.
internal static class DisplayText
{
    internal static string Ellipsis(string value, float width, Func<string,float> measure)
    {
        if(measure(value)<=width)return value;
        int[] starts=StringInfo.ParseCombiningCharacters(value);
        for(int n=starts.Length-1;n>0;n--)
        {string part=value[..starts[n]]+"…";if(measure(part)<=width)return part;}
        return "…";
    }
    internal static string Wrap(string value, float width, Func<string,float> measure)
    {
        var lines=new List<string>();
        foreach(string paragraph in value.Replace("\r","").Split('\n'))
        {
            string rest=paragraph;
            while(rest.Length>0 && measure(rest)>width)
            {
                int[] starts=StringInfo.ParseCombiningCharacters(rest);int end=starts.Length,lastSpace=-1;
                for(int n=1;n<=starts.Length;n++)
                {
                    int offset=n==starts.Length?rest.Length:starts[n];
                    if(measure(rest[..offset])>width){end=Math.Max(1,n-1);break;}
                    if(char.IsWhiteSpace(rest[offset-1]))lastSpace=offset;
                }
                int cut=lastSpace>0?lastSpace:end==starts.Length?rest.Length:starts[end];
                lines.Add(rest[..cut].TrimEnd());rest=rest[cut..].TrimStart();
            }
            lines.Add(rest);
        }
        return string.Join("\n",lines);
    }
    internal readonly record struct EmergencyLayout(int X,int Y,int Width,int Height,string Text,float Scale);
    internal readonly record struct NoticeLayout(int X,int Y,int Width,int Height,string Text);
    internal static NoticeLayout HudNotice(string value,int viewportWidth,int viewportHeight,float lineHeight,Func<string,float> measure)
    {
        // This is a passive handoff notice, not an action button. Wrap at the
        // active font size; only exceptionally small viewports truncate it.
        if(viewportWidth<=0||viewportHeight<=0||!float.IsFinite(lineHeight)||lineHeight<=0||string.IsNullOrEmpty(value))
            return new(0,0,0,0,"");
        int marginX=Math.Min(16,viewportWidth/4),marginY=Math.Min(16,viewportHeight/4);
        int width=viewportWidth-2*marginX;
        int rowHeight=(int)Math.Ceiling(lineHeight);
        int maxLines=(viewportHeight-2*marginY)/rowHeight;
        if(maxLines<1||measure("…")>width)return new(0,0,0,0,"");
        string[] wrapped=Wrap(value,width,measure).Split('\n');
        string[] lines=wrapped.Take(maxLines).Select(line=>Ellipsis(line,width,measure)).ToArray();
        if(wrapped.Length>maxLines)lines[^1]=Ellipsis(lines[^1]+" …",width,measure);
        int height=lines.Length*rowHeight;
        return new(marginX,viewportHeight-marginY-height,width,height,string.Join("\n",lines));
    }
    internal static EmergencyLayout Emergency(string value,int viewportWidth,int viewportHeight,float lineHeight,Func<string,float> measure)
    {
        // A corrupt translation must not produce an off-screen, thousands-line
        // safety button. Retain the same action with the independent English label.
        if(value.Length>512 || string.IsNullOrWhiteSpace(value))value="Give Up";
        int available=Math.Max(1,viewportWidth-24);
        int width=Math.Min(available,Math.Max(180,(int)Math.Ceiling(measure(value))+24));
        const float scale=1f;
        string text=Wrap(value,Math.Max(1,width-24),measure);
        int height=Math.Max(34,(int)Math.Ceiling(lineHeight)*text.Split('\n').Length+12);
        if(height>Math.Max(34,viewportHeight-48) && value!="Give Up")
            return Emergency("Give Up",viewportWidth,viewportHeight,lineHeight,measure);
        int x=(viewportWidth-width)/2,y=Math.Max(0,viewportHeight-height-35);
        return new(x,y,width,height,text,scale);
    }
}
