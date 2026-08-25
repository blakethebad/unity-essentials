namespace UnityEssentials.UI
{
    /// <summary>
    /// Marker interface for the payload handed to <see cref="UIBase.Show"/> and routed to an
    /// element's <c>OnShow</c>. The package never reads it — consumers define a small class per
    /// element that needs data and pattern-match it in <c>OnShow</c>. Null is always legal and every
    /// <c>OnShow</c> must tolerate it.
    /// </summary>
    public interface IUIData
    {
    }
}
