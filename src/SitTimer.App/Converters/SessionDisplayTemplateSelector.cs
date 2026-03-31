using System.Windows;
using System.Windows.Controls;
using SitTimer.App.ViewModels;

namespace SitTimer.App.Converters;

public class SessionDisplayTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StandaloneTemplate { get; set; }
    public DataTemplate? GroupHeaderTemplate { get; set; }
    public DataTemplate? GroupChildTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            StandaloneSessionItem => StandaloneTemplate,
            MergeGroupHeaderItem => GroupHeaderTemplate,
            MergeGroupChildItem => GroupChildTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
