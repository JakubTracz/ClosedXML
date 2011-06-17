﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClosedXML.Excel
{
    public interface IXLRichText: IXLFontBase, IEquatable<IXLRichText>
    {
        String Text { get; }

        IXLRichText SetBold(); IXLRichText SetBold(Boolean value);
        IXLRichText SetItalic(); IXLRichText SetItalic(Boolean value);
        IXLRichText SetUnderline(); IXLRichText SetUnderline(XLFontUnderlineValues value);
        IXLRichText SetStrikethrough(); IXLRichText SetStrikethrough(Boolean value);
        IXLRichText SetVerticalAlignment(XLFontVerticalTextAlignmentValues value);
        IXLRichText SetShadow(); IXLRichText SetShadow(Boolean value);
        IXLRichText SetFontSize(Double value);
        IXLRichText SetFontColor(IXLColor value);
        IXLRichText SetFontName(String value);
        IXLRichText SetFontFamilyNumbering(XLFontFamilyNumberingValues value);
    }
}