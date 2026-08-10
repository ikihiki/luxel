using System.Text;

namespace Luxel.Document;

/// <summary>単発の編集指定: 旧文書の <c>[From, To)</c> を <see cref="Insert"/> で置換する。純挿入は From==To、純削除は Insert=""。</summary>
public readonly record struct ChangeSpec(int From, int To, string Insert);

/// <summary>
/// 文書の変換を表す**変更セット** — 旧文書 (長さ <see cref="OldLength"/>) から新文書 (<see cref="NewLength"/>) への
/// 写像。retain (据え置き) と replace (置換) の**セクション列**で構成し、CodeMirror 6 の ChangeSet に倣う。
/// この 1 型が新スタックの位置写像の中心: <see cref="MapPos"/> が選択・装飾・非同期結果を同じ規則で移し、
/// <see cref="Compose"/> が連続編集を 1 つに畳み、<see cref="Invert"/> が undo を生む。
/// </summary>
public sealed class ChangeSet
{
    /// <summary>セクション: <see cref="Insert"/>==null なら旧文字を <see cref="OldLen"/> 個据え置き、
    /// != null なら旧 <see cref="OldLen"/> 文字を <see cref="Insert"/> で置換 (純挿入=OldLen 0 / 純削除=Insert "")。</summary>
    internal readonly record struct Sec(int OldLen, string? Insert);

    private readonly Sec[] _secs;

    internal ChangeSet(IReadOnlyList<Sec> secs)
    {
        // 隣接する同種セクションを畳んで正規化 (retain+retain, replace+replace)。空セクションは捨てる。
        var norm = new List<Sec>(secs.Count);
        foreach (Sec s in secs)
        {
            if (s.Insert is null)
            {
                if (s.OldLen <= 0) continue;
                if (norm.Count > 0 && norm[^1].Insert is null)
                    norm[^1] = new Sec(norm[^1].OldLen + s.OldLen, null);
                else norm.Add(s);
            }
            else
            {
                if (s.OldLen == 0 && s.Insert.Length == 0) continue;
                if (norm.Count > 0 && norm[^1].Insert is not null)
                    norm[^1] = new Sec(norm[^1].OldLen + s.OldLen, norm[^1].Insert + s.Insert);
                else norm.Add(s);
            }
        }
        _secs = norm.ToArray();

        int oldLen = 0, newLen = 0;
        foreach (Sec s in _secs)
        {
            oldLen += s.OldLen;
            newLen += s.Insert is null ? s.OldLen : s.Insert.Length;
        }
        OldLength = oldLen;
        NewLength = newLen;
    }

    /// <summary>写像元の文書長。</summary>
    public int OldLength { get; }
    /// <summary>写像先の文書長。</summary>
    public int NewLength { get; }
    /// <summary>何も変えない (全 retain) か。</summary>
    public bool IsEmpty => _secs.All(s => s.Insert is null);

    /// <summary>長さ <paramref name="length"/> の文書に対する恒等変更 (全据え置き)。</summary>
    public static ChangeSet Identity(int length)
        => length <= 0 ? new ChangeSet([]) : new ChangeSet([new Sec(length, null)]);

    /// <summary>ソート済み・非重複の編集列から変更セットを作る (順序が乱れていても From で並べ替える)。</summary>
    public static ChangeSet Of(int docLength, IReadOnlyList<ChangeSpec> edits)
    {
        if (edits.Count == 0) return Identity(docLength);
        var sorted = edits.OrderBy(e => e.From).ThenBy(e => e.To).ToArray();
        var b = new Builder();
        int pos = 0;
        foreach (ChangeSpec e in sorted)
        {
            int from = Math.Clamp(e.From, 0, docLength);
            int to = Math.Clamp(e.To, from, docLength);
            if (from < pos) throw new ArgumentException($"編集が重なっている ({from} < {pos})", nameof(edits));
            b.Retain(from - pos);
            b.Delete(to - from);
            b.Insert(e.Insert ?? "");
            pos = to;
        }
        b.Retain(docLength - pos);
        return b.Build();
    }

    /// <summary>旧文書に適用して新文書文字列を返す。</summary>
    public string Apply(string old)
    {
        if (old.Length != OldLength)
            throw new ArgumentException($"文書長不一致: ChangeSet は {OldLength} を期待、実際 {old.Length}", nameof(old));
        var sb = new StringBuilder(NewLength);
        int pos = 0;
        foreach (Sec s in _secs)
        {
            if (s.Insert is null) { sb.Append(old, pos, s.OldLen); pos += s.OldLen; }
            else { sb.Append(s.Insert); pos += s.OldLen; }
        }
        return sb.ToString();
    }

    /// <summary>旧文書の位置を新文書の位置へ写す。<paramref name="assoc"/> は境界での寄せ方
    /// (負=左/挿入の前、正=右/挿入の後)。削除範囲内は assoc 側の端へ潰れる。</summary>
    public int MapPos(int pos, int assoc = -1)
    {
        if (pos < 0) return 0;   // pos==0 は先頭挿入 (assoc>0 で挿入の後ろ) を扱うため通す
        int oldPos = 0, newPos = 0;
        for (int i = 0; i < _secs.Length; i++)
        {
            Sec s = _secs[i];
            if (s.Insert is null)   // 据え置き
            {
                int end = oldPos + s.OldLen;
                if (pos < end) return newPos + (pos - oldPos);
                if (pos == end && (assoc < 0 || i == _secs.Length - 1))
                    return newPos + s.OldLen;
                oldPos = end; newPos += s.OldLen;
            }
            else                    // 置換
            {
                int end = oldPos + s.OldLen;
                int ins = s.Insert.Length;
                if (pos <= end)
                {
                    if (pos == oldPos) return assoc < 0 ? newPos : newPos + ins;
                    return newPos + ins;   // 置換範囲の内側 or 終端 → 挿入の後ろ
                }
                oldPos = end; newPos += ins;
            }
        }
        return newPos;
    }

    /// <summary>この変更 (旧→新) を打ち消す逆変更 (新→旧) を、旧文書テキストから作る。undo に使う。</summary>
    public ChangeSet Invert(string old)
    {
        if (old.Length != OldLength)
            throw new ArgumentException($"文書長不一致: ChangeSet は {OldLength} を期待、実際 {old.Length}", nameof(old));
        var b = new Builder();
        int pos = 0;
        foreach (Sec s in _secs)
        {
            if (s.Insert is null) { b.Retain(s.OldLen); pos += s.OldLen; }
            else { b.Delete(s.Insert.Length); b.Insert(old.Substring(pos, s.OldLen)); pos += s.OldLen; }
        }
        return b.Build();
    }

    /// <summary>この変更 (旧→中間) の後に <paramref name="other"/> (中間→新) を適用した合成 (旧→新) を返す。
    /// 連続編集を 1 undo に畳む / 古い装飾を複数編集ぶんまとめて写す、に使う。</summary>
    public ChangeSet Compose(ChangeSet other)
    {
        if (NewLength != other.OldLength)
            throw new ArgumentException($"Compose 長不一致: {NewLength} vs {other.OldLength}");
        var b = new Builder();
        var mid = new MidWalker(other._secs);
        foreach (Sec s in _secs)
        {
            if (s.Insert is null)   // 中間へ「据え置き」で出た OldLen 文字を other が消費
            {
                for (int k = 0; k < s.OldLen; k++)
                {
                    b.Insert(mid.TakeInserts());
                    if (mid.Next()) b.Retain(1); else b.Delete(1);
                }
            }
            else                    // A が旧 OldLen 文字を削除し、Insert を中間へ挿入
            {
                b.Delete(s.OldLen);
                foreach (char c in s.Insert)
                {
                    b.Insert(mid.TakeInserts());
                    if (mid.Next()) b.Insert(c.ToString());   // other が据え置き → 結果でも挿入
                    // other が削除 → 挿入と相殺、何も出さない
                }
            }
        }
        b.Insert(mid.TakeInserts());   // 末尾の other 挿入
        return b.Build();
    }

    /// <summary>中間文書 (other の旧) を 1 文字ずつ歩き、各文字が据え置きか削除かを返す。
    /// 置換の挿入部は削除消費後に <see cref="TakeInserts"/> で回収する。</summary>
    private sealed class MidWalker
    {
        private readonly Sec[] _s;
        private int _i;
        private int _rem;
        private string _pending = "";

        public MidWalker(Sec[] secs)
        {
            _s = secs;
            _rem = _s.Length > 0 ? _s[0].OldLen : 0;
            Prime();
        }

        // 中間文字を消費し尽くしたセクションを飛ばし、飛ばした置換の挿入を pending へ溜める
        private void Prime()
        {
            while (_i < _s.Length && _rem == 0)
            {
                if (_s[_i].Insert is not null) _pending += _s[_i].Insert;
                _i++;
                if (_i < _s.Length) _rem = _s[_i].OldLen;
            }
        }

        public string TakeInserts()
        {
            if (_pending.Length == 0) return "";
            string p = _pending; _pending = ""; return p;
        }

        /// <summary>中間 1 文字を消費。true=据え置き / false=削除。</summary>
        public bool Next()
        {
            bool kept = _s[_i].Insert is null;
            _rem--;
            Prime();
            return kept;
        }
    }

    /// <inheritdoc/>
    public override string ToString()
        => "[" + string.Join(" ", _secs.Select(s => s.Insert is null ? $"r{s.OldLen}" : $"({s.OldLen}=>\"{s.Insert}\")")) + "]";

    /// <summary>retain/delete/insert を追記して隣接を畳む builder。delete+insert は 1 つの replace に合流する。</summary>
    private sealed class Builder
    {
        private readonly List<Sec> _secs = new();
        private int _pendDel;
        private StringBuilder? _pendIns;

        public void Retain(int n)
        {
            if (n <= 0) return;
            Flush();
            if (_secs.Count > 0 && _secs[^1].Insert is null)
                _secs[^1] = new Sec(_secs[^1].OldLen + n, null);
            else _secs.Add(new Sec(n, null));
        }

        public void Delete(int n) { if (n > 0) _pendDel += n; }

        public void Insert(string t) { if (!string.IsNullOrEmpty(t)) (_pendIns ??= new StringBuilder()).Append(t); }

        private void Flush()
        {
            if (_pendDel == 0 && _pendIns is null) return;
            _secs.Add(new Sec(_pendDel, _pendIns?.ToString() ?? ""));
            _pendDel = 0;
            _pendIns = null;
        }

        public ChangeSet Build() { Flush(); return new ChangeSet(_secs); }
    }
}
