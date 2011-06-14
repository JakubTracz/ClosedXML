﻿using DocumentFormat.OpenXml.Packaging;
using Ap = DocumentFormat.OpenXml.ExtendedProperties;
using Vt = DocumentFormat.OpenXml.VariantTypes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Op = DocumentFormat.OpenXml.CustomProperties;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;



namespace ClosedXML.Excel
{
    public partial class XLWorkbook
    {
        private void Load(String file)
        {
            LoadSheets(file);
        }
        private void Load(Stream stream)
        {
            LoadSheets(stream);
        }
        private void LoadSheets(String fileName)
        {
            using (SpreadsheetDocument dSpreadsheet = SpreadsheetDocument.Open(fileName, false))
            {
                LoadSpreadsheetDocument(dSpreadsheet);
            }
        }
        private void LoadSheets(Stream stream)
        {
            using (SpreadsheetDocument dSpreadsheet = SpreadsheetDocument.Open(stream, false))
            {
                LoadSpreadsheetDocument(dSpreadsheet);
            }
        }
        private void LoadSpreadsheetDocument(SpreadsheetDocument dSpreadsheet)
        {
            SetProperties(dSpreadsheet);
            //var sharedStrings = dSpreadsheet.WorkbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>();
            SharedStringItem[] sharedStrings = null;
            if (dSpreadsheet.WorkbookPart.GetPartsOfType<SharedStringTablePart>().Count() > 0)
            {
                SharedStringTablePart shareStringPart = dSpreadsheet.WorkbookPart.GetPartsOfType<SharedStringTablePart>().First();
                sharedStrings = shareStringPart.SharedStringTable.Elements<SharedStringItem>().ToArray();
            }

            if (dSpreadsheet.WorkbookPart.GetPartsOfType<CustomFilePropertiesPart>().Count() > 0)
            {
                CustomFilePropertiesPart customFilePropertiesPart = dSpreadsheet.WorkbookPart.GetPartsOfType<CustomFilePropertiesPart>().First();
                foreach (Op.CustomDocumentProperty m in customFilePropertiesPart.Properties.Elements<Op.CustomDocumentProperty>())
                {
                    String name = m.Name.Value;
                    if (m.VTLPWSTR != null)
                        CustomProperties.Add(name, m.VTLPWSTR.Text);
                    else if (m.VTFileTime != null)
                        CustomProperties.Add(name, DateTime.ParseExact(m.VTFileTime.Text, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture));
                    else if (m.VTDouble != null)
                        CustomProperties.Add(name, Double.Parse(m.VTDouble.Text, CultureInfo.InvariantCulture));
                    else if (m.VTBool != null)
                        CustomProperties.Add(name, m.VTBool.Text == "true");
                }
            }

            var referenceMode = dSpreadsheet.WorkbookPart.Workbook.CalculationProperties.ReferenceMode;
            if (referenceMode != null)
            {
                ReferenceStyle = referenceMode.Value.ToClosedXml();
            }

            var calculateMode = dSpreadsheet.WorkbookPart.Workbook.CalculationProperties.CalculationMode;
            if (calculateMode != null)
            {
                CalculateMode = calculateMode.Value.ToClosedXml();
            }

            if (dSpreadsheet.ExtendedFilePropertiesPart.Properties.Elements<Ap.Company>().Count() > 0)
                Properties.Company = dSpreadsheet.ExtendedFilePropertiesPart.Properties.GetFirstChild<Ap.Company>().Text;

            if (dSpreadsheet.ExtendedFilePropertiesPart.Properties.Elements<Ap.Manager>().Count() > 0)
                Properties.Manager = dSpreadsheet.ExtendedFilePropertiesPart.Properties.GetFirstChild<Ap.Manager>().Text;


            var workbookStylesPart = (WorkbookStylesPart)dSpreadsheet.WorkbookPart.WorkbookStylesPart;
            var s = (Stylesheet)workbookStylesPart.Stylesheet;
            
            NumberingFormats numberingFormats = s.NumberingFormats;
            //Int32 fillCount = (Int32)s.Fills.Count.Value;
            var fills = s.Fills;
            Borders borders = (Borders)s.Borders;
            Fonts fonts = (Fonts)s.Fonts;

            var sheets = dSpreadsheet.WorkbookPart.Workbook.Sheets;

            foreach (var sheet in sheets)
            {
                var sharedFormulasR1C1 = new Dictionary<UInt32, String>();

                Sheet dSheet = ((Sheet)sheet);
                WorksheetPart worksheetPart = (WorksheetPart)dSpreadsheet.WorkbookPart.GetPartById(dSheet.Id);

                var sheetName = dSheet.Name;

                var ws = (XLWorksheet)Worksheets.Add(sheetName);
                ws.RelId = dSheet.Id;
                ws.SheetId = (Int32)dSheet.SheetId.Value;

                
                if (dSheet.State != null)
                    ws.Visibility = dSheet.State.Value.ToClosedXml();

                var sheetFormatProperties = worksheetPart.Worksheet.SheetFormatProperties;
                if (sheetFormatProperties != null)
                {
                    if (sheetFormatProperties.DefaultRowHeight != null)
                        ws.RowHeight = sheetFormatProperties.DefaultRowHeight;

                    ws.RowHeightChanged = (sheetFormatProperties.CustomHeight != null && sheetFormatProperties.CustomHeight.Value);

                    if (sheetFormatProperties.DefaultColumnWidth != null)
                        ws.ColumnWidth = sheetFormatProperties.DefaultColumnWidth;
                }
                LoadSheetViews(worksheetPart, ws);
                var mergedCells = worksheetPart.Worksheet.Elements<MergeCells>().FirstOrDefault();
                if (mergedCells != null)
                {
                    foreach (MergeCell mergeCell in mergedCells.Elements<MergeCell>())
                    {
                        ws.Range(mergeCell.Reference).Merge();
                    }
                }

                #region LoadColumns
                var columns = worksheetPart.Worksheet.Elements<Columns>().FirstOrDefault();
                if (columns != null)
                {
                    
                    var wsDefaultColumn = columns.Elements<Column>().Where(c => c.Max == XLWorksheet.MaxNumberOfColumns).FirstOrDefault();
                    
                    if (wsDefaultColumn != null && wsDefaultColumn.Width != null) ws.ColumnWidth = wsDefaultColumn.Width - COLUMN_WIDTH_OFFSET;

                    Int32 styleIndexDefault = wsDefaultColumn != null && wsDefaultColumn.Style != null ? Int32.Parse(wsDefaultColumn.Style.InnerText) : -1;
                    if (styleIndexDefault >= 0)
                    {
                        ApplyStyle(ws, styleIndexDefault, s, fills, borders, fonts, numberingFormats);
                    }

                    foreach (var col in columns.Elements<Column>())
                    {
                        //IXLStylized toApply;
                        if (col.Max != XLWorksheet.MaxNumberOfColumns)
                        {
                            var xlColumns = (XLColumns)ws.Columns(col.Min, col.Max);
                            if (col.Width != null)
                                xlColumns.Width = col.Width - COLUMN_WIDTH_OFFSET;
                            else
                                xlColumns.Width = ws.ColumnWidth;

                            if (col.Hidden != null && col.Hidden)
                                xlColumns.Hide();

                            if (col.Collapsed != null && col.Collapsed)
                                xlColumns.CollapseOnly();

                            if (col.OutlineLevel != null)
                                xlColumns.ForEach(c => c.OutlineLevel = col.OutlineLevel);

                            Int32 styleIndex = col.Style != null ? Int32.Parse(col.Style.InnerText) : -1;
                            if (styleIndex > 0)
                            {
                                ApplyStyle(xlColumns, styleIndex, s, fills, borders, fonts, numberingFormats);
                            }
                            else
                            {
                                xlColumns.Style = DefaultStyle;
                            }
                        }
                    }
                }
                #endregion

                #region LoadRows
                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                foreach (var row in sheetData.Elements<Row>()) //.Where(r => r.CustomFormat != null && r.CustomFormat).Select(r => r))
                {
                    var xlRow = (XLRow)ws.Row((Int32)row.RowIndex.Value, false);
                    if (row.Height != null)
                        xlRow.Height = row.Height;
                    else
                        xlRow.Height = ws.RowHeight;

                    if (row.Hidden != null && row.Hidden)
                        xlRow.Hide();

                    if (row.Collapsed != null && row.Collapsed)
                        xlRow.Collapsed = true;

                    if (row.OutlineLevel != null && row.OutlineLevel > 0)
                        xlRow.OutlineLevel = row.OutlineLevel;

                    if (row.CustomFormat != null)
                    {
                        Int32 styleIndex = row.StyleIndex != null ? Int32.Parse(row.StyleIndex.InnerText) : -1;
                        if (styleIndex > 0)
                        {
                            ApplyStyle(xlRow, styleIndex, s, fills, borders, fonts, numberingFormats);
                        }
                        else
                        {
                            //((XLRow)xlRow).style = ws.Style;
                            //((XLRow)xlRow).SetStyleNoColumns(ws.Style);
                            xlRow.Style = DefaultStyle;
                            //xlRow.Style = ws.Style;
                        }
                    }

                    #region LoadCells
                    Dictionary<Int32, IXLStyle> styleList = new Dictionary<int, IXLStyle>();
                    styleList.Add(0, DefaultStyle);
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var dCell = (Cell)cell;
                        Int32 styleIndex = dCell.StyleIndex != null ? Int32.Parse(dCell.StyleIndex.InnerText) : 0;
                        var xlCell = (XLCell)ws.Cell(dCell.CellReference);

                        if (styleList.ContainsKey(styleIndex))
                            xlCell.Style = styleList[styleIndex];
                        else
                        {
                            ApplyStyle(xlCell, styleIndex, s, fills, borders, fonts, numberingFormats);
                            styleList.Add(styleIndex, xlCell.Style);
                        }


                        if (cell.CellFormula != null && cell.CellFormula.SharedIndex != null && cell.CellFormula.Reference != null)
                        {
                            String formula;
                            if (cell.CellFormula.FormulaType != null && cell.CellFormula.FormulaType == CellFormulaValues.Array)
                                formula = "{" + cell.CellFormula.Text + "}";
                            else
                                formula = cell.CellFormula.Text;

                            xlCell.FormulaA1 = formula;
                            sharedFormulasR1C1.Add(cell.CellFormula.SharedIndex.Value, xlCell.FormulaR1C1);

                            if (dCell.CellValue != null)
                                xlCell.ValueCached = dCell.CellValue.Text;
                        }
                        else if (dCell.CellFormula != null)
                        {
                            if (dCell.CellFormula.SharedIndex != null)
                            {
                                xlCell.FormulaR1C1 = sharedFormulasR1C1[dCell.CellFormula.SharedIndex.Value];
                            }
                            else
                            {
                                String formula;
                                if (cell.CellFormula.FormulaType != null && cell.CellFormula.FormulaType == CellFormulaValues.Array)
                                    formula = "{" + cell.CellFormula.Text + "}";
                                else
                                    formula = cell.CellFormula.Text;

                                xlCell.FormulaA1 = formula;
                            }

                            if (dCell.CellValue != null)
                                xlCell.ValueCached = dCell.CellValue.Text;
                        }
                        else if (dCell.DataType != null)
                        {
                            if (dCell.DataType == CellValues.InlineString)
                            {
                                xlCell.Value = dCell.InlineString.Text.Text;
                                xlCell.DataType = XLCellValues.Text;
                                xlCell.ShareString = false;
                            }
                            else if (dCell.DataType == CellValues.SharedString)
                            {
                                if (dCell.CellValue != null)
                                {
                                    if (!StringExtensions.IsNullOrWhiteSpace(dCell.CellValue.Text))
                                        xlCell.cellValue = sharedStrings[Int32.Parse(dCell.CellValue.Text)].InnerText;
                                    else
                                        xlCell.cellValue = dCell.CellValue.Text;
                                }
                                else
                                {
                                    xlCell.cellValue = String.Empty;
                                }
                                xlCell.DataType = XLCellValues.Text;
                            }
                            else if (dCell.DataType == CellValues.Date)
                            {
                                xlCell.Value = DateTime.FromOADate(Double.Parse(dCell.CellValue.Text, CultureInfo.InvariantCulture));
                            }
                            else if (dCell.DataType == CellValues.Boolean)
                            {
                                xlCell.Value = (dCell.CellValue.Text == "1");
                            }
                            else if (dCell.DataType == CellValues.Number)
                            {
                                xlCell.Value = Double.Parse(dCell.CellValue.Text, CultureInfo.InvariantCulture);
                                var numberFormatId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).NumberFormatId;
                                if (numberFormatId == 46U)
                                    xlCell.DataType = XLCellValues.TimeSpan;
                                else
                                    xlCell.DataType = XLCellValues.Number;
                            }
                        }
                        else if (dCell.CellValue != null)
                        {
                            var numberFormatId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).NumberFormatId;
                            Double val = Double.Parse(dCell.CellValue.Text, CultureInfo.InvariantCulture);
                            xlCell.Value = val;
                            if (s.NumberingFormats != null && s.NumberingFormats.Any(nf => ((NumberingFormat)nf).NumberFormatId.Value == numberFormatId))
                                xlCell.Style.NumberFormat.Format =
                                    ((NumberingFormat)s.NumberingFormats.Where(nf => ((NumberingFormat)nf).NumberFormatId.Value == numberFormatId).Single()).FormatCode.Value;
                            else
                                xlCell.Style.NumberFormat.NumberFormatId = Int32.Parse(numberFormatId);


                            if (!StringExtensions.IsNullOrWhiteSpace(xlCell.Style.NumberFormat.Format))
                                xlCell.DataType = GetDataTypeFromFormat(xlCell.Style.NumberFormat.Format);
                            else
                                if ((numberFormatId >= 14 && numberFormatId <= 22) || (numberFormatId >= 45 && numberFormatId <= 47))
                                    xlCell.DataType = XLCellValues.DateTime;
                                else if (numberFormatId == 49)
                                    xlCell.DataType = XLCellValues.Text;
                                else
                                    xlCell.DataType = XLCellValues.Number;
                        }
                    }
                    #endregion
                }
                #endregion

                

                #region LoadTables
                foreach (var tablePart in worksheetPart.TableDefinitionParts)
                {
                    var dTable = (Table)tablePart.Table;
                    var reference = dTable.Reference.Value;
                    var xlTable = ws.Range(reference).CreateTable(dTable.Name);
                    if (dTable.TotalsRowCount != null && dTable.TotalsRowCount.Value > 0)
                        ((XLTable)xlTable).showTotalsRow = true;

                    if (dTable.TableStyleInfo != null)
                    {
                        if (dTable.TableStyleInfo.ShowFirstColumn != null)
                            xlTable.EmphasizeFirstColumn = dTable.TableStyleInfo.ShowFirstColumn.Value;
                        if (dTable.TableStyleInfo.ShowLastColumn != null)
                            xlTable.EmphasizeLastColumn = dTable.TableStyleInfo.ShowLastColumn.Value;
                        if (dTable.TableStyleInfo.ShowRowStripes != null)
                            xlTable.ShowRowStripes = dTable.TableStyleInfo.ShowRowStripes.Value;
                        if (dTable.TableStyleInfo.ShowColumnStripes != null)
                            xlTable.ShowColumnStripes = dTable.TableStyleInfo.ShowColumnStripes.Value;
                        if (dTable.TableStyleInfo.Name != null)
                            xlTable.Theme = (XLTableTheme)Enum.Parse(typeof(XLTableTheme), dTable.TableStyleInfo.Name.Value);
                    }

                    xlTable.ShowAutoFilter = dTable.AutoFilter != null;

                    foreach (var column in dTable.TableColumns)
                    {
                        var tableColumn = (TableColumn)column;
                        if (tableColumn.TotalsRowFunction != null)
                            xlTable.Field(tableColumn.Name.Value).TotalsRowFunction = tableColumn.TotalsRowFunction.Value.ToClosedXml();

                        if (tableColumn.TotalsRowFormula != null)
                            xlTable.Field(tableColumn.Name.Value).TotalsRowFormulaA1 = tableColumn.TotalsRowFormula.Text;

                        if (tableColumn.TotalsRowLabel != null)
                            xlTable.Field(tableColumn.Name.Value).TotalsRowLabel = tableColumn.TotalsRowLabel.Value;
                    }
                }
                #endregion

                LoadAutoFilter(worksheetPart, ws);;

                LoadSheetProtection(worksheetPart, ws);;

                LoadDataValidations(worksheetPart, ws);;

                LoadHyperlinks(worksheetPart, ws);

                LoadPrintOptions(worksheetPart, ws);;

                LoadPageMargins(worksheetPart, ws);;

                LoadPageSetup(worksheetPart, ws);;

                LoadHeaderFooter(worksheetPart, ws);;

                LoadSheetProperties(worksheetPart, ws);;

                LoadRowBreaks(worksheetPart, ws);;

                LoadColumnBreaks(worksheetPart, ws);;
            }

            var workbook = (Workbook)dSpreadsheet.WorkbookPart.Workbook;

            var workbookView = (WorkbookView)workbook.BookViews.FirstOrDefault();
            if (workbookView != null && workbookView.ActiveTab != null)
                Worksheet((Int32)(workbookView.ActiveTab.Value + 1)).SetTabActive();
            
            if (workbook.DefinedNames != null)
            {
                foreach (DefinedName definedName in workbook.DefinedNames)
                {
                    var name = definedName.Name;
                    if (name == "_xlnm.Print_Area")
                    {
                        foreach (var area in definedName.Text.Split(','))
                        {
                            var sections = area.Trim().Split('!');
                            var sheetName = sections[0].Replace("\'", "");
                            var sheetArea = sections[1];
                            if (!sheetArea.Equals("#REF"))
                                Worksheets.Worksheet(sheetName).PageSetup.PrintAreas.Add(sheetArea);
                        }
                    }
                    else if (name == "_xlnm.Print_Titles")
                    {
                        var areas = definedName.Text.Split(',');

                        var colSections = areas[0].Trim().Split('!');
                        var sheetNameCol = colSections[0].Replace("\'", "");
                        var sheetAreaCol = colSections[1];
                        if (!sheetAreaCol.Equals("#REF"))
                            Worksheets.Worksheet(sheetNameCol).PageSetup.SetColumnsToRepeatAtLeft(sheetAreaCol);

                        var rowSections = areas[1].Split('!');
                        var sheetNameRow = rowSections[0].Replace("\'", "");
                        var sheetAreaRow = rowSections[1];
                        if (!sheetAreaRow.Equals("#REF"))
                            Worksheets.Worksheet(sheetNameRow).PageSetup.SetRowsToRepeatAtTop(sheetAreaRow);
                    }
                    else
                    {

                        var text = definedName.Text;

                        if (!text.Equals("#REF"))
                        {
                            var localSheetId = definedName.LocalSheetId;
                            var comment = definedName.Comment;
                            if (localSheetId == null)
                            {
                                NamedRanges.Add(name, text, comment);
                            }
                            else
                            {
                                Worksheet(Int32.Parse(localSheetId) + 1).NamedRanges.Add(name, text, comment);
                            }
                        }
                    }
                }
            }
        }

        private XLCellValues GetDataTypeFromFormat(String format)
        {
            var length = format.Length;
            String f = format.ToLower();
            for (Int32 i = 0; i < length; i++)
            {
                Char c = f[i];
                if (c == '"')
                    i = f.IndexOf('"', i + 1);
                else if (c == '0' || c == '#' || c == '?')
                    return XLCellValues.Number;
                else if (c == 'y' || c == 'm' || c == 'd' || c == 'h' || c == 's')
                    return XLCellValues.DateTime;
            }
            return XLCellValues.Text;
        }

        private void LoadAutoFilter(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            AutoFilter af = worksheetPart.Worksheet.Elements<AutoFilter>().FirstOrDefault();
            if (af != null)
                ws.Range(af.Reference.Value).SetAutoFilter();
        }

        private void LoadSheetProtection(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var sp = worksheetPart.Worksheet.Elements<SheetProtection>().FirstOrDefault();
            if (sp != null)
            {
                if (sp.Sheet != null) ws.Protection.Protected = sp.Sheet.Value;
                if (sp.Password != null) (ws.Protection as XLSheetProtection).PasswordHash = sp.Password.Value;
                if (sp.FormatCells != null) ws.Protection.FormatCells = sp.FormatCells.Value;
                if (sp.FormatColumns != null) ws.Protection.FormatColumns = sp.FormatColumns.Value;
                if (sp.FormatRows != null) ws.Protection.FormatRows = sp.FormatRows.Value;
                if (sp.InsertColumns != null) ws.Protection.InsertColumns = sp.InsertColumns.Value;
                if (sp.InsertHyperlinks != null) ws.Protection.InsertHyperlinks = sp.InsertHyperlinks.Value;
                if (sp.InsertRows != null) ws.Protection.InsertRows = sp.InsertRows.Value;
                if (sp.DeleteColumns != null) ws.Protection.DeleteColumns = sp.DeleteColumns.Value;
                if (sp.DeleteRows != null) ws.Protection.DeleteRows = sp.DeleteRows.Value;
                if (sp.AutoFilter != null) ws.Protection.AutoFilter = sp.AutoFilter.Value;
                if (sp.PivotTables != null) ws.Protection.PivotTables = sp.PivotTables.Value;
                if (sp.Sort != null) ws.Protection.Sort = sp.Sort.Value;
                if (sp.SelectLockedCells != null) ws.Protection.SelectLockedCells = !sp.SelectLockedCells.Value;
                if (sp.SelectUnlockedCells != null) ws.Protection.SelectUnlockedCells = !sp.SelectUnlockedCells.Value;
            }
        }

        private void LoadDataValidations(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var dataValidations = worksheetPart.Worksheet.Elements<DataValidations>().FirstOrDefault();
            if (dataValidations != null)
            {
                foreach (var dvs in dataValidations.Elements<DataValidation>())
                {
                    foreach (String rangeAddress in dvs.SequenceOfReferences.InnerText.Split(' '))
                    {
                        var dvt = ws.Range(rangeAddress).DataValidation;
                        if (dvs.AllowBlank != null) dvt.IgnoreBlanks = dvs.AllowBlank;
                        if (dvs.ShowDropDown != null) dvt.InCellDropdown = !dvs.ShowDropDown.Value;
                        if (dvs.ShowErrorMessage != null) dvt.ShowErrorMessage = dvs.ShowErrorMessage;
                        if (dvs.ShowInputMessage != null) dvt.ShowInputMessage = dvs.ShowInputMessage;
                        if (dvs.PromptTitle != null) dvt.InputTitle = dvs.PromptTitle;
                        if (dvs.Prompt != null) dvt.InputMessage = dvs.Prompt;
                        if (dvs.ErrorTitle != null) dvt.ErrorTitle = dvs.ErrorTitle;
                        if (dvs.Error != null) dvt.ErrorMessage = dvs.Error;
                        if (dvs.ErrorStyle != null) dvt.ErrorStyle = dvs.ErrorStyle.Value.ToClosedXml();
                        if (dvs.Type != null) dvt.AllowedValues = dvs.Type.Value.ToClosedXml();
                        if (dvs.Operator != null) dvt.Operator = dvs.Operator.Value.ToClosedXml();
                        if (dvs.Formula1 != null) dvt.MinValue = dvs.Formula1.Text;
                        if (dvs.Formula2 != null) dvt.MaxValue = dvs.Formula2.Text;
                    }
                }
            }
        }

        private void LoadHyperlinks(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var hyperlinkDictionary = new Dictionary<String, Uri>();
            if (worksheetPart.HyperlinkRelationships != null)
                hyperlinkDictionary = worksheetPart.HyperlinkRelationships.ToDictionary(hr => hr.Id, hr => hr.Uri);

            var hyperlinks = worksheetPart.Worksheet.Elements<Hyperlinks>().FirstOrDefault();
            if (hyperlinks != null)
            {
                foreach (var hl in hyperlinks.Elements<Hyperlink>())
                {
                    if (!hl.Reference.Value.Equals("#REF"))
                    {
                        String tooltip = hl.Tooltip != null ? tooltip = hl.Tooltip.Value : tooltip = String.Empty;
                        var xlRange = ws.Range(hl.Reference.Value);
                        foreach (XLCell xlCell in xlRange.Cells())
                        {
                            xlCell.SettingHyperlink = true;
                            if (hl.Id != null)
                                xlCell.Hyperlink = new XLHyperlink(hyperlinkDictionary[hl.Id], tooltip);
                            else
                                xlCell.Hyperlink = new XLHyperlink(hl.Location.Value, tooltip);
                            xlCell.SettingHyperlink = false;
                        }
                    }
                }
            }
        }

        private void LoadColumnBreaks(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var rWS = worksheetPart.Worksheet;
            var bs = rWS.Elements<ColumnBreaks>();
            ColumnBreaks columnBreaks = bs.FirstOrDefault();
            //try
            //{
            //    columnBreaks = bs[0];
            //}
            //catch { }

            if (columnBreaks != null)
            {
                foreach (var columnBreak in columnBreaks.Elements<Break>())
                {
                    if (columnBreak.Id != null)
                        ws.PageSetup.ColumnBreaks.Add(Int32.Parse(columnBreak.Id.InnerText));
                }
            }
        }

        private void LoadRowBreaks(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var rowBreaks = worksheetPart.Worksheet.Elements<RowBreaks>().FirstOrDefault();
            if (rowBreaks != null)
            {
                foreach (var rowBreak in rowBreaks.Elements<Break>())
                {
                    ws.PageSetup.RowBreaks.Add(Int32.Parse(rowBreak.Id.InnerText));
                }
            }
        }

        private void LoadSheetProperties(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var sheetProperty = worksheetPart.Worksheet.Elements<SheetProperties>().FirstOrDefault();
            if (sheetProperty != null)
            {
                if (sheetProperty.TabColor != null)
                    ws.TabColor = GetColor(sheetProperty.TabColor);

                if (sheetProperty.OutlineProperties != null)
                {
                    if (sheetProperty.OutlineProperties.SummaryBelow != null)
                    {
                        ws.Outline.SummaryVLocation = sheetProperty.OutlineProperties.SummaryBelow ?
                            XLOutlineSummaryVLocation.Bottom : XLOutlineSummaryVLocation.Top;
                    }

                    if (sheetProperty.OutlineProperties.SummaryRight != null)
                    {
                        ws.Outline.SummaryHLocation = sheetProperty.OutlineProperties.SummaryRight ?
                            XLOutlineSummaryHLocation.Right : XLOutlineSummaryHLocation.Left;
                    }
                }
            }
        }

        private void LoadHeaderFooter(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var headerFooter = worksheetPart.Worksheet.Elements<HeaderFooter>().FirstOrDefault();
            if (headerFooter != null)
            {
                if (headerFooter.AlignWithMargins != null)
                    ws.PageSetup.AlignHFWithMargins = headerFooter.AlignWithMargins;
                if (headerFooter.ScaleWithDoc != null)
                    ws.PageSetup.ScaleHFWithDocument = headerFooter.ScaleWithDoc;

                // Footers
                var xlFooter = (XLHeaderFooter)ws.PageSetup.Footer;
                var evenFooter = (EvenFooter)headerFooter.EvenFooter;
                if (evenFooter != null)
                    xlFooter.SetInnerText(XLHFOccurrence.EvenPages, evenFooter.Text);
                var oddFooter = (OddFooter)headerFooter.OddFooter;
                if (oddFooter != null)
                    xlFooter.SetInnerText(XLHFOccurrence.OddPages, oddFooter.Text);
                var firstFooter = (FirstFooter)headerFooter.FirstFooter;
                if (firstFooter != null)
                    xlFooter.SetInnerText(XLHFOccurrence.FirstPage, firstFooter.Text);
                // Headers
                var xlHeader = (XLHeaderFooter)ws.PageSetup.Header;
                var evenHeader = (EvenHeader)headerFooter.EvenHeader;
                if (evenHeader != null)
                    xlHeader.SetInnerText(XLHFOccurrence.EvenPages, evenHeader.Text);
                var oddHeader = (OddHeader)headerFooter.OddHeader;
                if (oddHeader != null)
                    xlHeader.SetInnerText(XLHFOccurrence.OddPages, oddHeader.Text);
                var firstHeader = (FirstHeader)headerFooter.FirstHeader;
                if (firstHeader != null)
                    xlHeader.SetInnerText(XLHFOccurrence.FirstPage, firstHeader.Text);
            }
        }

        private void LoadPageSetup(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var pageSetup = worksheetPart.Worksheet.Elements<PageSetup>().FirstOrDefault();
            if (pageSetup != null)
            {
                if (pageSetup.PaperSize != null)
                    ws.PageSetup.PaperSize = (XLPaperSize)Int32.Parse(pageSetup.PaperSize.InnerText);
                if (pageSetup.Scale != null)
                {
                    ws.PageSetup.Scale = Int32.Parse(pageSetup.Scale.InnerText);
                }
                else
                {
                    if (pageSetup.FitToWidth != null)
                        ws.PageSetup.PagesWide = Int32.Parse(pageSetup.FitToWidth.InnerText);
                    if (pageSetup.FitToHeight != null)
                        ws.PageSetup.PagesTall = Int32.Parse(pageSetup.FitToHeight.InnerText);
                }
                if (pageSetup.PageOrder != null)
                    ws.PageSetup.PageOrder = pageSetup.PageOrder.Value.ToClosedXml();
                if (pageSetup.Orientation != null)
                    ws.PageSetup.PageOrientation = pageSetup.Orientation.Value.ToClosedXml();
                if (pageSetup.BlackAndWhite != null)
                    ws.PageSetup.BlackAndWhite = pageSetup.BlackAndWhite;
                if (pageSetup.Draft != null)
                    ws.PageSetup.DraftQuality = pageSetup.Draft;
                if (pageSetup.CellComments != null)
                    ws.PageSetup.ShowComments = pageSetup.CellComments.Value.ToClosedXml();
                if (pageSetup.Errors != null)
                    ws.PageSetup.PrintErrorValue = pageSetup.Errors.Value.ToClosedXml();
                if (pageSetup.HorizontalDpi != null) ws.PageSetup.HorizontalDpi = (Int32)pageSetup.HorizontalDpi.Value;
                if (pageSetup.VerticalDpi != null) ws.PageSetup.VerticalDpi = (Int32)pageSetup.VerticalDpi.Value;
                if (pageSetup.FirstPageNumber != null) ws.PageSetup.FirstPageNumber = Int32.Parse(pageSetup.FirstPageNumber.InnerText);
            }
        }

        private void LoadPageMargins(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var pageMargins = worksheetPart.Worksheet.Elements<PageMargins>().FirstOrDefault();
            if (pageMargins != null)
            {
                if (pageMargins.Bottom != null)
                    ws.PageSetup.Margins.Bottom = pageMargins.Bottom;
                if (pageMargins.Footer != null)
                    ws.PageSetup.Margins.Footer = pageMargins.Footer;
                if (pageMargins.Header != null)
                    ws.PageSetup.Margins.Header = pageMargins.Header;
                if (pageMargins.Left != null)
                    ws.PageSetup.Margins.Left = pageMargins.Left;
                if (pageMargins.Right != null)
                    ws.PageSetup.Margins.Right = pageMargins.Right;
                if (pageMargins.Top != null)
                    ws.PageSetup.Margins.Top = pageMargins.Top;
            }
        }

        private void LoadPrintOptions(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var printOptions = worksheetPart.Worksheet.Elements<PrintOptions>().FirstOrDefault();
            if (printOptions != null)
            {
                if (printOptions.GridLines != null)
                    ws.PageSetup.ShowGridlines = printOptions.GridLines;
                if (printOptions.HorizontalCentered != null)
                    ws.PageSetup.CenterHorizontally = printOptions.HorizontalCentered;
                if (printOptions.VerticalCentered != null)
                    ws.PageSetup.CenterVertically = printOptions.VerticalCentered;
                if (printOptions.Headings != null)
                    ws.PageSetup.ShowRowAndColumnHeadings = printOptions.Headings;
            }
        }

        private void LoadSheetViews(WorksheetPart worksheetPart, XLWorksheet ws)
        {
            SheetViews sheetViews = worksheetPart.Worksheet.SheetViews;
            if (sheetViews != null)
            {
                SheetView sheetView = sheetViews.Elements<SheetView>().FirstOrDefault();
                if (sheetView != null)
                {
                    if (sheetView.ShowFormulas != null) ws.ShowFormulas = sheetView.ShowFormulas.Value;
                    if (sheetView.ShowGridLines != null) ws.ShowGridLines = sheetView.ShowGridLines.Value;
                    if (sheetView.ShowOutlineSymbols != null) ws.ShowOutlineSymbols = sheetView.ShowOutlineSymbols.Value;
                    if (sheetView.ShowRowColHeaders != null) ws.ShowRowColHeaders = sheetView.ShowRowColHeaders.Value;
                    if (sheetView.ShowRuler != null) ws.ShowRuler = sheetView.ShowRuler.Value;
                    if (sheetView.ShowWhiteSpace != null) ws.ShowWhiteSpace = sheetView.ShowWhiteSpace.Value;
                    if (sheetView.ShowZeros != null) ws.ShowZeros = sheetView.ShowZeros.Value;
                    if (sheetView.TabSelected != null) ws.TabSelected = sheetView.TabSelected.Value;

                    var pane = (Pane)sheetView.Elements<Pane>().FirstOrDefault();
                    if (pane != null)
                    {
                        if (pane.State != null && (pane.State == PaneStateValues.FrozenSplit || pane.State == PaneStateValues.Frozen))
                        {
                            if (pane.HorizontalSplit != null)
                                ws.SheetView.SplitColumn = (Int32)pane.HorizontalSplit.Value;
                            if (pane.VerticalSplit != null)
                                ws.SheetView.SplitRow = (Int32)pane.VerticalSplit.Value;
                        }
                    }
                }
            }
        }

        private void SetProperties(SpreadsheetDocument dSpreadsheet)
        {
            var p = dSpreadsheet.PackageProperties;
            Properties.Author = p.Creator;
            Properties.Category = p.Category;
            Properties.Comments = p.Description;
            if (p.Created != null)
                Properties.Created = p.Created.Value;
            Properties.Keywords = p.Keywords;
            Properties.LastModifiedBy = p.LastModifiedBy;
            Properties.Status = p.ContentStatus;
            Properties.Subject = p.Subject;
            Properties.Title = p.Title;
        }

        private Dictionary<String, System.Drawing.Color> colorList = new Dictionary<string, System.Drawing.Color>();
        private IXLColor GetColor(ColorType color)
        {
            IXLColor retVal = null;
            if (color != null)
            {
                if (color.Rgb != null)
                {
                    String htmlColor = "#" + color.Rgb.Value;
                    System.Drawing.Color thisColor;    
                    if (!colorList.ContainsKey(htmlColor))
                    {
                        thisColor = System.Drawing.ColorTranslator.FromHtml(htmlColor);
                        colorList.Add(htmlColor, thisColor);
                    }
                    else
                    {
                        thisColor = colorList[htmlColor];
                    }
                    retVal = new XLColor(thisColor);
                }
                else if (color.Indexed != null && color.Indexed < 64)
                {
                    retVal = new XLColor((Int32)color.Indexed.Value);
                }
                else if (color.Theme != null)
                {
                    if (color.Tint != null)
                        retVal = XLColor.FromTheme((XLThemeColor)color.Theme.Value, color.Tint.Value);
                    else
                        retVal = XLColor.FromTheme((XLThemeColor)color.Theme.Value);
                }
            }
            if (retVal == null)
                return new XLColor();
            else
                return retVal;
        }

        private void ApplyStyle(IXLStylized xlStylized, Int32 styleIndex, Stylesheet s, Fills fills, Borders borders, Fonts fonts, NumberingFormats numberingFormats)
        {
            var cellFormat = (CellFormat)s.CellFormats.ElementAt(styleIndex);
            
            if (cellFormat.ApplyProtection != null)
            {
                Protection protection = cellFormat.Protection;

                if (protection == null)
                    xlStylized.InnerStyle.Protection = new XLProtection(null, DefaultStyle.Protection);
                else
                {
                    xlStylized.InnerStyle.Protection.Hidden = protection.Hidden != null && protection.Hidden.HasValue && protection.Hidden.Value;
                    xlStylized.InnerStyle.Protection.Locked = protection.Locked == null || (protection.Locked != null && protection.Locked.HasValue && protection.Locked.Value);
                }
            }

            var fillId = cellFormat.FillId.Value;
            if (fillId > 0)
            {
                var fill = (Fill)fills.ElementAt((Int32)fillId);
                if (fill.PatternFill != null)
                {
                    if (fill.PatternFill.PatternType != null)
                        xlStylized.InnerStyle.Fill.PatternType = fill.PatternFill.PatternType.Value.ToClosedXml();

                    var fgColor = GetColor(fill.PatternFill.ForegroundColor);
                    if (fgColor.HasValue) xlStylized.InnerStyle.Fill.PatternColor = fgColor;

                    var bgColor = GetColor(fill.PatternFill.BackgroundColor);
                    if (bgColor.HasValue) 
                        xlStylized.InnerStyle.Fill.PatternBackgroundColor = bgColor;
                }
            }

            //var alignmentDictionary = GetAlignmentDictionary(s);

            //if (alignmentDictionary.ContainsKey(styleIndex))
            //{
            //    var alignment = alignmentDictionary[styleIndex];
            var alignment = cellFormat.Alignment;
            if (alignment != null)
            {
                if (alignment.Horizontal != null)
                    xlStylized.InnerStyle.Alignment.Horizontal = alignment.Horizontal.Value.ToClosedXml();
                if (alignment.Indent != null)
                    xlStylized.InnerStyle.Alignment.Indent = Int32.Parse(alignment.Indent.ToString());
                if (alignment.JustifyLastLine != null)
                    xlStylized.InnerStyle.Alignment.JustifyLastLine = alignment.JustifyLastLine;
                if (alignment.ReadingOrder != null)
                    xlStylized.InnerStyle.Alignment.ReadingOrder = (XLAlignmentReadingOrderValues)Int32.Parse(alignment.ReadingOrder.ToString());
                if (alignment.RelativeIndent != null)
                    xlStylized.InnerStyle.Alignment.RelativeIndent = alignment.RelativeIndent;
                if (alignment.ShrinkToFit != null)
                    xlStylized.InnerStyle.Alignment.ShrinkToFit = alignment.ShrinkToFit;
                if (alignment.TextRotation != null)
                    xlStylized.InnerStyle.Alignment.TextRotation = (Int32)alignment.TextRotation.Value;
                if (alignment.Vertical != null)
                    xlStylized.InnerStyle.Alignment.Vertical = alignment.Vertical.Value.ToClosedXml();
                if (alignment.WrapText !=null)
                    xlStylized.InnerStyle.Alignment.WrapText = alignment.WrapText;
            }


            //if (borders.ContainsKey(styleIndex))
            //{
            //    var border = borders[styleIndex];
            var borderId = cellFormat.BorderId.Value;
            var border = (Border)borders.ElementAt((Int32)borderId);
            if (border != null)
            {
                var bottomBorder = (BottomBorder)border.BottomBorder;
                if (bottomBorder != null)
                {
                    if (bottomBorder.Style != null)
                        xlStylized.InnerStyle.Border.BottomBorder = bottomBorder.Style.Value.ToClosedXml();

                    var bottomBorderColor = GetColor(bottomBorder.Color);
                    if (bottomBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.BottomBorderColor = bottomBorderColor;
                }
                var topBorder = (TopBorder)border.TopBorder;
                if (topBorder != null)
                {
                    if (topBorder.Style != null)
                        xlStylized.InnerStyle.Border.TopBorder = topBorder.Style.Value.ToClosedXml();
                    var topBorderColor = GetColor(topBorder.Color);
                    if (topBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.TopBorderColor = topBorderColor;
                }
                var leftBorder = (LeftBorder)border.LeftBorder;
                if (leftBorder != null)
                {
                    if (leftBorder.Style != null)
                        xlStylized.InnerStyle.Border.LeftBorder = leftBorder.Style.Value.ToClosedXml();
                    var leftBorderColor = GetColor(leftBorder.Color);
                    if (leftBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.LeftBorderColor = leftBorderColor;
                }
                var rightBorder = (RightBorder)border.RightBorder;
                if (rightBorder != null)
                {
                    if (rightBorder.Style != null)
                        xlStylized.InnerStyle.Border.RightBorder = rightBorder.Style.Value.ToClosedXml();
                    var rightBorderColor = GetColor(rightBorder.Color);
                    if (rightBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.RightBorderColor = rightBorderColor;
                }
                var diagonalBorder = (DiagonalBorder)border.DiagonalBorder;
                if (diagonalBorder != null)
                {
                    if (diagonalBorder.Style != null)
                        xlStylized.InnerStyle.Border.DiagonalBorder = diagonalBorder.Style.Value.ToClosedXml();
                    var diagonalBorderColor = GetColor(diagonalBorder.Color);
                    if (diagonalBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.DiagonalBorderColor = diagonalBorderColor;
                    if (border.DiagonalDown != null)
                        xlStylized.InnerStyle.Border.DiagonalDown = border.DiagonalDown;
                    if (border.DiagonalUp != null)
                        xlStylized.InnerStyle.Border.DiagonalUp = border.DiagonalUp;
                }
            }

            //if (fonts.ContainsKey(styleIndex))
            //{
            //    var font = fonts[styleIndex];
            var fontId = cellFormat.FontId;
            var font = (Font)fonts.ElementAt((Int32)fontId.Value);
            if (font != null)
            {
                xlStylized.InnerStyle.Font.Bold = GetBoolean(font.Bold);

                var fontColor = GetColor(font.Color);
                if (fontColor.HasValue)
                    xlStylized.InnerStyle.Font.FontColor = fontColor;

                if (font.FontFamilyNumbering != null && ((FontFamilyNumbering)font.FontFamilyNumbering).Val != null)
                    xlStylized.InnerStyle.Font.FontFamilyNumbering = (XLFontFamilyNumberingValues)Int32.Parse(((FontFamilyNumbering)font.FontFamilyNumbering).Val.ToString());
                if (font.FontName != null)
                {
                    if (((FontName)font.FontName).Val != null)
                        xlStylized.InnerStyle.Font.FontName = ((FontName)font.FontName).Val;
                }
                if (font.FontSize != null)
                {
                    if (((FontSize)font.FontSize).Val != null)
                        xlStylized.InnerStyle.Font.FontSize = ((FontSize)font.FontSize).Val;
                }

                xlStylized.InnerStyle.Font.Italic = GetBoolean(font.Italic);
                xlStylized.InnerStyle.Font.Shadow = GetBoolean(font.Shadow);
                xlStylized.InnerStyle.Font.Strikethrough = GetBoolean(font.Strike);
                
                if (font.Underline != null)
                    if (font.Underline.Val != null)
                        xlStylized.InnerStyle.Font.Underline = ((Underline)font.Underline).Val.Value.ToClosedXml();
                    else
                        xlStylized.InnerStyle.Font.Underline = XLFontUnderlineValues.Single;

                if (font.VerticalTextAlignment != null)
                    
                if (font.VerticalTextAlignment.Val != null)
                    xlStylized.InnerStyle.Font.VerticalAlignment = ((VerticalTextAlignment)font.VerticalTextAlignment).Val.Value.ToClosedXml();
                else
                    xlStylized.InnerStyle.Font.VerticalAlignment = XLFontVerticalTextAlignmentValues.Baseline;
            }

                var numberFormatId = cellFormat.NumberFormatId;
                if (numberFormatId != null)
                {
                    var formatCode = String.Empty;
                    if (numberingFormats != null)
                    {
                        var numberFormatList = numberingFormats.Where(nf => ((NumberingFormat)nf).NumberFormatId != null && ((NumberingFormat)nf).NumberFormatId.Value == numberFormatId);

                        if (numberFormatList.Count() > 0)
                        {
                            NumberingFormat numberingFormat = (NumberingFormat)numberFormatList.First();
                            if (numberingFormat.FormatCode != null)
                                formatCode = numberingFormat.FormatCode.Value;
                        }
                    }
                    if (formatCode.Length > 0)
                        xlStylized.InnerStyle.NumberFormat.Format = formatCode;
                    else
                        xlStylized.InnerStyle.NumberFormat.NumberFormatId = (Int32)numberFormatId.Value;
                }
            
        }

        private Boolean GetBoolean(BooleanPropertyType property)
        {
            if (property != null)
            {
                if (property.Val != null)
                    return property.Val;
                else
                    return true;
            }
            else
            {
                return false;
            }
        }




    }
}