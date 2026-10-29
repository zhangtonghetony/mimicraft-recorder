namespace Mimicraft.Recorder.Core;

public sealed record InkPoint(double X,double Y);
public sealed record Annotation(string Tool,InkPoint[] Points,string Color="#FF394A",double Width=4,string Text="");
public sealed class AnnotationDocument
{
    private Annotation[] _items=[];
    private readonly Stack<Annotation[]> _undo=new(),_redo=new();
    public IReadOnlyList<Annotation> Items=>Array.AsReadOnly(_items);
    public event Action? Changed;
    public void Set(IEnumerable<Annotation> items){_undo.Push(_items);_redo.Clear();_items=items.ToArray();Changed?.Invoke();}
    public void Add(Annotation item)=>Set(_items.Append(item));
    public void Remove(int index)=>Set(_items.Where((_,i)=>i!=index));
    public void Move(int index,double dx,double dy)=>Set(_items.Select((a,i)=>i!=index?a:a with {Points=a.Points.Select(p=>new InkPoint(p.X+dx,p.Y+dy)).ToArray()}));
    public void Clear()=>Set([]);
    public void Undo(){if(_undo.TryPop(out var old)){_redo.Push(_items);_items=old;Changed?.Invoke();}}
    public void Redo(){if(_redo.TryPop(out var next)){_undo.Push(_items);_items=next;Changed?.Invoke();}}
}
