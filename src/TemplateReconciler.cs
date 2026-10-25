using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DailyDiaryCard;

internal static class TemplateReconciler
{
    public static IReadOnlyList<Heading> Parse(string source)
    {
        var headings=new MarkdownDoc(source).Sections;
        if(headings.Count==0)throw new IOException("模板中没有二级标题，已保留上一版栏目。");
        if(headings.Select(h=>h.Title).Distinct().Count()!=headings.Count)throw new IOException("模板含有重复二级标题，已保留上一版栏目。");
        return headings;
    }
    private static string BaseTitle(string title)=>Regex.Replace(Regex.Replace(title,@"（[^）]*）|\([^)]*\)",""),@"\s+","");
    private static string Normalize(string text,string nl)=>text.Replace("\r\n","\n").Replace('\r','\n').Replace("\n",nl);
    private static bool Marker(string text)=>text.Contains("<!-- TASKS:START -->")||text.Contains("<!-- TASKS:END -->");
    public static string Update(string source,string previous,string next,out string notice)
    {
        var current=new MarkdownDoc(source);var old=Parse(previous);var fresh=Parse(next);string nl=current.NewLine;
        var bodies=new Dictionary<string,string>();var matched=new List<Heading>();var renames=new Dictionary<string,string>();bool retained=false;
        foreach(var heading in fresh)
        {
            var oldHeading=old.SingleOrDefault(h=>h.Title==heading.Title);
            var existing=current.Unique(heading.Title);
            if(existing==null)
            {
                var candidates=old.Where(h=>!fresh.Any(n=>n.Title==h.Title)&&BaseTitle(h.Title)==BaseTitle(heading.Title)).ToArray();
                if(candidates.Length==1&&fresh.Count(h=>BaseTitle(h.Title)==BaseTitle(heading.Title))==1)
                {oldHeading=candidates[0];existing=current.Unique(oldHeading.Title);if(existing!=null)renames[existing.Title]=heading.Title;}
            }
            string actual=existing?.Title??heading.Title;
            string body=existing==null?Normalize(heading.Body,nl):oldHeading==null?existing.Body:MergeBody(oldHeading.Body,heading.Body,existing.Body,nl,ref retained);
            bodies[actual]=body;matched.Add(heading with {Title=actual});
        }
        var result=current.Apply(bodies,matched);
        foreach(var pair in renames)
        {
            var parsed=new MarkdownDoc(result);var h=parsed.Unique(pair.Key)!;
            result=result.Remove(h.Start,h.RawHeading.Length).Insert(h.Start,"## "+pair.Value+nl);
        }
        foreach(var marker in new[]{"<!-- TASKS:START -->","<!-- TASKS:END -->"})
            if(Count(result,marker)<Count(source,marker))throw new IOException("模板更新会删除原有任务区域标记，已暂停同步并保留日记。");
        notice=retained?"已填写或无法确认的段落保持原样。":"";
        if(new MarkdownDoc(result).Sections.Any(h=>!fresh.Any(t=>t.Title==h.Title)))notice+="模板外的原有栏目已保留，可在卡片底部查看。";
        return result;
    }
    private static int Count(string text,string value)=>(text.Length-text.Replace(value,"").Length)/value.Length;
    private static string MergeBody(string before,string after,string current,string nl,ref bool retained)
    {
        before=Normalize(before,nl);after=Normalize(after,nl);
        if(before==after)return current;
        if(Normalize(current,nl)==before&&!new[]{"<!-- TASKS:START -->","<!-- TASKS:END -->"}.Any(m=>Count(after,m)<Count(before,m)))return after;
        var a=MarkdownDoc.Lines(before).Select(l=>l.Raw).ToArray();var b=MarkdownDoc.Lines(after).Select(l=>l.Raw).ToArray();
        if((long)a.Length*b.Length>1_000_000){retained=true;return current;}
        var dp=new int[a.Length+1,b.Length+1];
        for(int x=a.Length-1;x>=0;x--)for(int y=b.Length-1;y>=0;y--)dp[x,y]=a[x]==b[y]?dp[x+1,y+1]+1:Math.Max(dp[x+1,y],dp[x,y+1]);
        var edits=new List<(int Start,int Length,string Value)>();int i=0,j=0;
        while(i<a.Length||j<b.Length)
        {
            if(i<a.Length&&j<b.Length&&a[i]==b[j]){i++;j++;continue;}
            int start=i,from=j;
            while(i<a.Length||j<b.Length)
            {
                if(i<a.Length&&j<b.Length&&a[i]==b[j])break;
                if(j<b.Length&&(i==a.Length||dp[i,j+1]>=dp[i+1,j]))j++;else i++;
            }
            string removed=string.Concat(a[start..i]),inserted=string.Concat(b[from..j]);int position=-1;
            if(Marker(removed)||Marker(inserted)){retained=true;continue;}
            // Only update a still-intact template prefix. A matching phrase elsewhere may be
            // the user's own prose or a code example, rather than an unchanged template hint.
            string prefix=string.Concat(a[..start]);
            if(current.StartsWith(prefix,StringComparison.Ordinal))
            {
                if(removed.Length>0&&current.AsSpan(prefix.Length).StartsWith(removed.AsSpan(),StringComparison.Ordinal))position=prefix.Length;
                else if(removed.Length==0&&!string.IsNullOrWhiteSpace(inserted))position=prefix.Length;
            }
            if(position<0){retained=true;continue;}
            if(edits.Any(e=>position<e.Start+e.Length&&position+removed.Length>e.Start)){retained=true;continue;}
            edits.Add((position,removed.Length,inserted));
        }
        foreach(var edit in edits.OrderByDescending(e=>e.Start))current=current.Remove(edit.Start,edit.Length).Insert(edit.Start,edit.Value);
        return current;
    }
}
