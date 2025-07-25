﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Win32;
using Style = DocumentFormat.OpenXml.Wordprocessing.Style;

namespace Sep490ClassDocumentGenerator
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<FileSystemNode> _fileSystemNodes;
        private IgnoreManager? _ignoreManager;
        private string _rootFolder;

        public MainWindow()
        {
            InitializeComponent();
            _fileSystemNodes = new ObservableCollection<FileSystemNode>();
            FileTreeView.ItemsSource = _fileSystemNodes;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Source Folder",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                _rootFolder = dialog.FolderName;
                FolderPathTextBox.Text = dialog.FolderName;
                LoadFileSystem(dialog.FolderName);
            }
        }

        // Browse output file path
        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Word Documents (*.docx)|*.docx",
                FileName = "ClassSpecifications.docx",
                DefaultExt = "docx"
            };

            if (dialog.ShowDialog() == true) { OutputFileTextBox.Text = dialog.FileName; }
        }

        // Load folder structure into TreeView
        private void LoadFileSystem(string path)
        {
            _fileSystemNodes.Clear();
            _ignoreManager = new IgnoreManager(path);
            if (Directory.Exists(path))
            {
                var rootNode = new FileSystemNode(path, true);
                PopulateNode(rootNode, path);
                _fileSystemNodes.Add(rootNode);
            }
            else
            {
                MessageBox.Show("Selected path does not exist or is not accessible.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
       
        

        // Populate TreeView nodes
        private void PopulateNode(FileSystemNode node, string path)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var relativeDir = Path.GetRelativePath(FolderPathTextBox.Text, dir).Replace('\\', '/');
                    if (_ignoreManager?.IsIgnored(relativeDir) == true) continue;

                    var dirNode = new FileSystemNode(dir, true);
                    node.Children.Add(dirNode);
                    PopulateNode(dirNode, dir);
                }

                foreach (var file in Directory.GetFiles(path, "*.cs"))
                {
                    var relativeFile = Path.GetRelativePath(FolderPathTextBox.Text, file).Replace('\\', '/');
                    if (_ignoreManager?.IsIgnored(relativeFile) == true) continue;

                    node.Children.Add(new FileSystemNode(file, false));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Generate document
        private void GenerateDocButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FolderPathTextBox.Text))
            {
                MessageBox.Show("Please select a source folder.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(OutputFileTextBox.Text))
            {
                MessageBox.Show("Please specify an output file path.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate starting index
            if (!int.TryParse(StartIndexTextBox.Text, out int startIndex) || startIndex < 1)
            {
                MessageBox.Show("Please enter a valid positive integer for the starting index.",
                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedFiles = GetSelectedFiles(_fileSystemNodes);
            if (!selectedFiles.Any())
            {
                MessageBox.Show("No .cs files selected.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var classInfos = new List<ClassInfo>();
                foreach (var file in selectedFiles) 
                { 
                    ProcessFile(file, classInfos); 
                }

                var selectionWindow = new ClassSelectionWindow(classInfos, OutputFileTextBox.Text, startIndex);
                selectionWindow.OnGenerate = (selectedClasses, outputPath, startIdx) =>
                {
                    try
                    {
                        CreateWordDoc(selectedClasses, outputPath, startIdx);
                        MessageBox.Show("Document generated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error generating document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                selectionWindow.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show($"Error generating document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // Get selected .cs files
        private List<string> GetSelectedFiles(IEnumerable<FileSystemNode> nodes)
        {
            var files = new List<string>();
            foreach (var node in nodes)
            {
                if (node.IsSelected && !node.IsDirectory && node.Path.EndsWith(".cs")) { files.Add(node.Path); }
                files.AddRange(GetSelectedFiles(node.Children));
            }
            return files;
        }

        // Process a single .cs file
        private void ProcessFile(string file, List<ClassInfo> classInfos)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();

            var namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>();
            foreach (var ns in namespaces)
            {
                foreach (var typeDecl in ns.Members.OfType<TypeDeclarationSyntax>())
                {
                    ProcessClassOrInterface(typeDecl, ns.Name.ToString(), classInfos, null);
                }
            }
            
            foreach (var typeDecl in root.Members.OfType<TypeDeclarationSyntax>())
            {
                ProcessClassOrInterface(typeDecl, "Global", classInfos, null);
            }
        }

        private void ProcessClassOrInterface(TypeDeclarationSyntax decl, string namespaceName, List<ClassInfo> classInfos, string parentClass)
        {
            string fullClassName = parentClass == null ? decl.Identifier.Text : $"{parentClass}.{decl.Identifier.Text}";

            var classInfo = new ClassInfo
            {
                ClassName = fullClassName,
                Namespace = namespaceName,
                Summary = GetXmlSummary(decl)
            };

            var members = decl.Members;
            bool isInInterface = decl is InterfaceDeclarationSyntax;

            // Properties
            foreach (var prop in members.OfType<PropertyDeclarationSyntax>())
            {
                classInfo.Attributes.Add(new ClassMember
                {
                    Name = prop.Identifier.Text,
                    Type = prop.Type.ToString(),
                    Visibility = GetVisibility(prop.Modifiers, isInInterface),
                    Summary = GetXmlSummary(prop)
                });
            }
            
            // Fields 
            foreach (var field in members.OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    classInfo.Attributes.Add(new ClassMember
                    {
                        Name = variable.Identifier.Text,
                        Type = field.Declaration.Type.ToString(),
                        Visibility = GetVisibility(field.Modifiers, isInInterface),
                        Summary = GetXmlSummary(field)
                    });
                }
            }

            // Methods
            foreach (var method in members.OfType<MethodDeclarationSyntax>())
            {
                var paramDescriptions = GetXmlParamDescriptions(method);

                classInfo.Methods.Add(new MethodMember
                {
                    Name = method.Identifier.Text,
                    ReturnType = method.ReturnType.ToString(),
                    Visibility = GetVisibility(method.Modifiers, isInInterface),
                    Summary = GetXmlSummary(method),
                    Parameters = method.ParameterList.Parameters.Select(p => new ParameterInfo
                    {
                        Name = p.Identifier.Text,
                        Type = p.Type?.ToString() ?? "",
                        Summary = paramDescriptions.TryGetValue(p.Identifier.Text, out var desc) ? desc : ""
                    }).ToList()
                });
            }

            classInfos.Add(classInfo);

            // ✅ Nested classes/interfaces
            foreach (var nested in members.OfType<TypeDeclarationSyntax>()) { ProcessClassOrInterface(nested, namespaceName, classInfos, fullClassName); }
        }

        private string GetXmlSummary(MemberDeclarationSyntax member)
        {
            var trivia = member.GetLeadingTrivia()
                .Select(i => i.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            if (trivia == null) return "";

            var summaryElement = trivia.Content.OfType<XmlElementSyntax>()
                .FirstOrDefault(e => e.StartTag.Name.LocalName.Text == "summary");

            return summaryElement?.Content.ToString().Trim().Replace("///", "").Trim() ?? "";
        }

        private Dictionary<string, string> GetXmlParamDescriptions(MemberDeclarationSyntax member)
        {
            var result = new Dictionary<string, string>();

            var trivia = member.GetLeadingTrivia()
                .Select(i => i.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            if (trivia == null) return result;

            var paramElements = trivia.Content.OfType<XmlElementSyntax>()
                .Where(e => e.StartTag.Name.LocalName.Text == "param");

            foreach (var param in paramElements)
            {
                var nameAttr = param.StartTag.Attributes
                    .OfType<XmlNameAttributeSyntax>()
                    .FirstOrDefault();

                if (nameAttr != null)
                {
                    var paramName = nameAttr.Identifier.Identifier.Text;
                    var text = param.Content.ToString().Trim();
                    result[paramName] = text;
                }
            }

            return result;
        }


        private void ProcessInterface(InterfaceDeclarationSyntax interfaceDecl, string namespaceName, List<ClassInfo> classInfos, string parentInterface)
        {
            string fullInterfaceName = parentInterface == null ? interfaceDecl.Identifier.Text : $"{parentInterface}.{interfaceDecl.Identifier.Text}";

            var interfaceInfo = new ClassInfo
            {
                ClassName = fullInterfaceName,
                Namespace = namespaceName,
                IsInterface = true
            };

            foreach (var prop in interfaceDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                interfaceInfo.Attributes.Add(new ClassMember
                {
                    Name = prop.Identifier.Text,
                    Type = prop.Type.ToString(),
                    Visibility = "public", // Interface properties mặc định là public
                    Summary = ""
                });
            }

            foreach (var method in interfaceDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                interfaceInfo.Methods.Add(new MethodMember
                {
                    Name = method.Identifier.Text,
                    ReturnType = method.ReturnType.ToString(),
                    Visibility = "public", // Interface methods mặc định là public
                    Summary = "",
                    Parameters = method.ParameterList.Parameters
                        .Select(p => new ParameterInfo
                        {
                            Name = p.Identifier.Text,
                            Type = p.Type?.ToString() ?? "",
                            Summary = ""
                        }).ToList()
                });
            }

            classInfos.Add(interfaceInfo);

            // Xử lý interface lồng nhau nếu có
            foreach (var nestedInterface in interfaceDecl.Members.OfType<InterfaceDeclarationSyntax>())
            {
                ProcessInterface(nestedInterface, namespaceName, classInfos, fullInterfaceName);
            }
        }


        // Original GetVisibility method
        string GetVisibility(SyntaxTokenList modifiers, bool isInInterface = false)
        {
            if (isInInterface) return "public";

            if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
            if (modifiers.Any(SyntaxKind.PrivateKeyword)) return "private";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return "protected";
            if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
            return "private"; // fallback
        }


        // Modified CreateWordDoc to use custom startIndex
        private void CreateWordDoc(List<ClassInfo> classes, string filePath, int startIndex)
        {
            using (WordprocessingDocument doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());
                var body = mainPart.Document.Body;

                CreateStyles(mainPart);

                // Use custom startIndex for the main title
                var titlePara = new Paragraph(new Run(new Text($"{startIndex}. Class Specifications")));
                titlePara.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId()
                {
                    Val = "Heading2"
                });
                body.AppendChild(titlePara);

                var groupedClasses = classes.GroupBy(c => c.Namespace);
                int sectionIndex = 1;
                foreach (var namespaceGroup in groupedClasses)
                {
                    string displayNamespace = string.Join("/", namespaceGroup.Key.Split('.').Skip(1));
                    var namespacePara = new Paragraph(new Run(new Text($"{startIndex}.{sectionIndex} {displayNamespace}")));
                    namespacePara.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId()
                    {
                        Val = "Heading3"
                    });
                    body.AppendChild(namespacePara);

                    int classIndex = 1;
                    foreach (var classInfo in namespaceGroup)
                    {
                        // Use custom startIndex for class subsections
                        var classPara = new Paragraph(new Run(new Text($"{startIndex}.{sectionIndex}.{classIndex} {classInfo.ClassName}")));
                        classPara.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId()
                        {
                            Val = "Heading4"
                        });
                        body.AppendChild(classPara);

                        var table = new Table();
                        
                        var tableGrid = new TableGrid(
                            new GridColumn() { Width = "1000" },   // khoảng 10% (OpenXML dùng đơn vị twentieth of a point)
                            new GridColumn() { Width = "3000" },
                            new GridColumn() { Width = "6000" }
                        );
                        
                        table.AppendChild(tableGrid);

                        var tableProps = new TableProperties(
                            new TableWidth()
                            {
                                Width = "5000",
                                Type = TableWidthUnitValues.Pct
                            },
                            new TableLayout()
                            {
                                Type = TableLayoutValues.Fixed 
                            },
                            new TableBorders(
                                new TopBorder
                                {
                                    Val = new EnumValue<BorderValues>(BorderValues.Single),
                                    Size = 12
                                },
                                new BottomBorder
                                {
                                    Val = new EnumValue<BorderValues>(BorderValues.Single),
                                    Size = 12
                                },
                                new LeftBorder
                                {
                                    Val = new EnumValue<BorderValues>(BorderValues.Single),
                                    Size = 12
                                },
                                new RightBorder
                                {
                                    Val = new EnumValue<BorderValues>(BorderValues.Single),
                                    Size = 12
                                },
                                new InsideHorizontalBorder
                                {
                                    Val = new EnumValue<BorderValues>(BorderValues.Single),
                                    Size = 12
                                },
                                new InsideVerticalBorder
                                {
                                    Val = new EnumValue<BorderValues>(BorderValues.Single),
                                    Size = 12
                                }
                            )
                        );
                        table.AppendChild(tableProps);

                        var headerRow = new TableRow();
                        headerRow.AppendChild(CreateTableCell("No", true, "FFE8E1"));
                        headerRow.AppendChild(CreateTableCell("Name", true, "FFE8E1"));
                        headerRow.AppendChild(CreateTableCell("Description", true, "FFE8E1"));
                        table.AppendChild(headerRow);

                        var attributesHeaderRow = new TableRow();
                        attributesHeaderRow.AppendChild(
                            new TableCell(
                                new TableCellProperties(
                                    new GridSpan()
                                    {
                                        Val = 3
                                    },
                                    new Shading
                                    {
                                        Val = ShadingPatternValues.Clear,
                                        Color = "auto",
                                        Fill = "FFE8E1"
                                    },
                                    new TableCellMargin(
                                        new LeftMargin()
                                        {
                                            Width = "100",
                                            Type = TableWidthUnitValues.Dxa
                                        },
                                        new RightMargin()
                                        {
                                            Width = "100",
                                            Type = TableWidthUnitValues.Dxa
                                        },
                                        new TopMargin()
                                        {
                                            Width = "50",
                                            Type = TableWidthUnitValues.Dxa
                                        },
                                        new BottomMargin()
                                        {
                                            Width = "50",
                                            Type = TableWidthUnitValues.Dxa
                                        }
                                    )
                                ),
                                new Paragraph(
                                    new ParagraphProperties(new Justification()
                                    {
                                        Val = JustificationValues.Left
                                    }),
                                    new Run(new RunProperties(new Bold()), new Text("Attributes"))
                                )
                            )
                        );
                        table.AppendChild(attributesHeaderRow);

                        int attrIndex = 1;
                        foreach (var attr in classInfo.Attributes)
                        {
                            var attrRow = new TableRow();
                            attrRow.AppendChild(CreateTableCell(attrIndex.ToString("D2")));
                            attrRow.AppendChild(CreateTableCell($"{attr.Name}"));

                            var descParagraph = new Paragraph();
                            descParagraph.Append(
                                CreateBoldUnderlineRun("Visibility:"),
                                CreateNormalRun(" " + attr.Visibility),
                                new Run(new Break()),
                                CreateBoldUnderlineRun("Type: "),
                                CreateNormalRun(" " + attr.Type)
                            );

                            if (!string.IsNullOrWhiteSpace(attr.Summary))
                            {
                                descParagraph.Append(
                                    new Run(new Break()),
                                    CreateBoldUnderlineRun("Description:"),
                                    CreateNormalRun(" " + attr.Summary)
                                );
                            }

                            var descCell = new TableCell(descParagraph);
                            descCell.TableCellProperties = new TableCellProperties(
                                new TableCellMargin(
                                    new LeftMargin()
                                    {
                                        Width = "100",
                                        Type = TableWidthUnitValues.Dxa
                                    },
                                    new RightMargin()
                                    {
                                        Width = "100",
                                        Type = TableWidthUnitValues.Dxa
                                    },
                                    new TopMargin()
                                    {
                                        Width = "50",
                                        Type = TableWidthUnitValues.Dxa
                                    },
                                    new BottomMargin()
                                    {
                                        Width = "50",
                                        Type = TableWidthUnitValues.Dxa
                                    }
                                )
                            );
                            attrRow.AppendChild(descCell);

                            table.AppendChild(attrRow);
                            attrIndex++;
                        }

                        var descriptionHeaderRow = new TableRow();
                        descriptionHeaderRow.AppendChild(
                            new TableCell(
                                new TableCellProperties(
                                    new GridSpan()
                                    {
                                        Val = 3
                                    },
                                    new Shading
                                    {
                                        Val = ShadingPatternValues.Clear,
                                        Color = "auto",
                                        Fill = "FFE8E1"
                                    },
                                    new TableCellMargin(
                                        new LeftMargin()
                                        {
                                            Width = "100",
                                            Type = TableWidthUnitValues.Dxa
                                        },
                                        new RightMargin()
                                        {
                                            Width = "100",
                                            Type = TableWidthUnitValues.Dxa
                                        },
                                        new TopMargin()
                                        {
                                            Width = "50",
                                            Type = TableWidthUnitValues.Dxa
                                        },
                                        new BottomMargin()
                                        {
                                            Width = "50",
                                            Type = TableWidthUnitValues.Dxa
                                        }
                                    )
                                ),
                                new Paragraph(
                                    new ParagraphProperties(new Justification()
                                    {
                                        Val = JustificationValues.Left
                                    }),
                                    new Run(new RunProperties(new Bold()), new Text("Methods/Operations"))
                                )
                            )
                        );
                        table.AppendChild(descriptionHeaderRow);

                        int methodIndex = 1;
                        foreach (var method in classInfo.Methods)
                        {
                            var methodRow = new TableRow();
                            methodRow.AppendChild(CreateTableCell(methodIndex.ToString("D2")));
                            methodRow.AppendChild(CreateTableCell($"{method.Name}"));

                            var descParagraph = new Paragraph();
                            descParagraph.Append(
                                CreateBoldUnderlineRun("Visibility:"),
                                CreateNormalRun($" {method.Visibility}"),
                                new Run(new Break()),
                                CreateBoldUnderlineRun("Return: "),
                                CreateNormalRun($" {method.ReturnType}")
                            );

                            if (!string.IsNullOrWhiteSpace(method.Summary))
                            {
                                descParagraph.Append(
                                    new Run(new Break()),
                                    CreateBoldUnderlineRun("Description: "),
                                    CreateNormalRun(" " + method.Summary)
                                );
                            }

                            descParagraph.Append(
                                new Run(new Break()),
                                CreateBoldUnderlineRun("Parameters:")
                            );

                            if (method.Parameters.Any())
                            {
                                descParagraph.Append( new Run(new Break()));
                                foreach (var param in method.Parameters)
                                {
                                    var summary = string.IsNullOrWhiteSpace(param.Summary)
                                        ? ""
                                        : $", {param.Summary}";

                                    descParagraph.Append(
                                        CreateNormalRun($"- {param.Name}: {param.Type}{summary}"),
                                        new Run(new Break())
                                    );
                                }
                            }

                            else { descParagraph.Append(CreateNormalRun("None")); }

                            var descCell = new TableCell(descParagraph);
                            descCell.TableCellProperties = new TableCellProperties(
                                new TableCellMargin(
                                    new LeftMargin()
                                    {
                                        Width = "100",
                                        Type = TableWidthUnitValues.Dxa
                                    },
                                    new RightMargin()
                                    {
                                        Width = "100",
                                        Type = TableWidthUnitValues.Dxa
                                    },
                                    new TopMargin()
                                    {
                                        Width = "50",
                                        Type = TableWidthUnitValues.Dxa
                                    },
                                    new BottomMargin()
                                    {
                                        Width = "50",
                                        Type = TableWidthUnitValues.Dxa
                                    }
                                )
                            );
                            methodRow.AppendChild(descCell);

                            table.AppendChild(methodRow);
                            methodIndex++;
                        }

                        body.AppendChild(table);
                        body.AppendChild(new Paragraph());

                        classIndex++;
                    }

                    sectionIndex++;
                }

                doc.Save();
            }
        }

        // Original CreateStyles method
        private void CreateStyles(MainDocumentPart mainPart)
        {
            var stylePart = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();

            stylePart.Styles = new Styles(
                new DocDefaults(
                    new RunPropertiesDefault(
                        new RunPropertiesBaseStyle(
                            new RunFonts()
                            {
                                Ascii = "Times New Roman",
                                HighAnsi = "Times New Roman"
                            },
                            new FontSize()
                            {
                                Val = "22"
                            }
                        )
                    )
                ),
                new Style()
                {
                    Type = StyleValues.Paragraph,
                    StyleId = "Heading2",
                    BasedOn = new BasedOn()
                    {
                        Val = "Normal"
                    },
                    NextParagraphStyle = new NextParagraphStyle()
                    {
                        Val = "Normal"
                    },
                    PrimaryStyle = new PrimaryStyle(),
                    StyleName = new StyleName()
                    {
                        Val = "heading 2"
                    },
                    StyleRunProperties = new StyleRunProperties(
                        new Bold(),
                        new FontSize()
                        {
                            Val = "28"
                        },
                        new RunFonts()
                        {
                            Ascii = "Times New Roman",
                            HighAnsi = "Times New Roman"
                        }
                    ),
                    StyleParagraphProperties = new StyleParagraphProperties(
                        new OutlineLevel()
                        {
                            Val = 0
                        },
                        new SpacingBetweenLines()
                        {
                            After = "200"
                        },
                        new KeepNext()
                    )
                },
                new Style()
                {
                    Type = StyleValues.Paragraph,
                    StyleId = "Heading3",
                    BasedOn = new BasedOn()
                    {
                        Val = "Normal"
                    },
                    NextParagraphStyle = new NextParagraphStyle()
                    {
                        Val = "Normal"
                    },
                    PrimaryStyle = new PrimaryStyle(),
                    StyleName = new StyleName()
                    {
                        Val = "heading 3"
                    },
                    StyleRunProperties = new StyleRunProperties(
                        new Bold(),
                        new FontSize()
                        {
                            Val = "26"
                        },
                        new RunFonts()
                        {
                            Ascii = "Times New Roman",
                            HighAnsi = "Times New Roman"
                        }
                    ),
                    StyleParagraphProperties = new StyleParagraphProperties(
                        new OutlineLevel()
                        {
                            Val = 1
                        },
                        new SpacingBetweenLines()
                        {
                            After = "180"
                        },
                        new KeepNext()
                    )
                },
                new Style()
                {
                    Type = StyleValues.Paragraph,
                    StyleId = "Heading4",
                    BasedOn = new BasedOn()
                    {
                        Val = "Normal"
                    },
                    NextParagraphStyle = new NextParagraphStyle()
                    {
                        Val = "Normal"
                    },
                    PrimaryStyle = new PrimaryStyle(),
                    StyleName = new StyleName()
                    {
                        Val = "heading 4"
                    },
                    StyleRunProperties = new StyleRunProperties(
                        new Bold(),
                        new FontSize()
                        {
                            Val = "22"
                        },
                        new RunFonts()
                        {
                            Ascii = "Times New Roman",
                            HighAnsi = "Times New Roman"
                        }
                    ),
                    StyleParagraphProperties = new StyleParagraphProperties(
                        new OutlineLevel()
                        {
                            Val = 2
                        },
                        new SpacingBetweenLines()
                        {
                            After = "160"
                        },
                        new KeepNext()
                    )
                },
                new Style()
                {
                    Type = StyleValues.Paragraph,
                    StyleId = "Normal",
                    Default = OnOffValue.FromBoolean(true),
                    StyleName = new StyleName()
                    {
                        Val = "Normal"
                    },
                    StyleRunProperties = new StyleRunProperties(
                        new FontSize()
                        {
                            Val = "22"
                        },
                        new RunFonts()
                        {
                            Ascii = "Times New Roman",
                            HighAnsi = "Times New Roman"
                        }
                    ),
                    StyleParagraphProperties = new StyleParagraphProperties(
                        new SpacingBetweenLines()
                        {
                            After = "120"
                        }
                    )
                }
            );
        }

        // Original CreateBoldUnderlineRun method
        private static Run CreateBoldUnderlineRun(string text)
        {
            return new Run(
                new RunProperties(
                    new Bold(),
                    new Underline()
                    {
                        Val = UnderlineValues.Single
                    },
                    new RunFonts()
                    {
                        Ascii = "Times New Roman"
                    },
                    new FontSize()
                    {
                        Val = "22"
                    }
                ),
                new Text(text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }
            );
        }

        // Original CreateNormalRun method
        private static Run CreateNormalRun(string text)
        {
            return new Run(
                new RunProperties(
                    new RunFonts()
                    {
                        Ascii = "Times New Roman"
                    },
                    new FontSize()
                    {
                        Val = "22"
                    }
                ),
                new Text(text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }
            );
        }

        // Original CreateTableCell method
        private static TableCell CreateTableCell(string text, bool isHeader = false, string backgroundColor = null)
        {
            var cell = new TableCell();

            var cellProps = new TableCellProperties(
                new TableCellMargin(
                    new LeftMargin()
                    {
                        Width = "50",
                        Type = TableWidthUnitValues.Dxa
                    },
                    new RightMargin()
                    {
                        Width = "50",
                        Type = TableWidthUnitValues.Dxa
                    },
                    new TopMargin()
                    {
                        Width = "50",
                        Type = TableWidthUnitValues.Dxa
                    },
                    new BottomMargin()
                    {
                        Width = "50",
                        Type = TableWidthUnitValues.Dxa
                    }
                )
            );

            if (!string.IsNullOrEmpty(backgroundColor))
            {
                cellProps.Shading = new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = backgroundColor
                };
            }

            cell.AppendChild(cellProps);

            if (text.Contains('\n'))
            {
                var lines = text.Split('\n');
                var para = new Paragraph();

                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) { para.AppendChild(new Break()); }
                    var run = new Run(
                        new RunProperties(
                            new FontSize()
                            {
                                Val = "22"
                            },
                            new RunFonts()
                            {
                                Ascii = "Times New Roman"
                            }
                        ),
                        new Text(lines[i])
                    );
                    para.AppendChild(run);
                }

                if (isHeader)
                {
                    para.ParagraphProperties = new ParagraphProperties(
                        new Justification()
                        {
                            Val = JustificationValues.Left
                        }
                    );

                    foreach (var run in para.Elements<Run>()) { run.RunProperties.Bold = new Bold(); }
                }

                cell.Append(para);
            }
            else
            {
                var run = new Run(
                    new RunProperties(
                        new FontSize()
                        {
                            Val = "22"
                        },
                        new RunFonts()
                        {
                            Ascii = "Times New Roman"
                        }
                    ),
                    new Text(text)
                );

                var para = new Paragraph(run);

                if (isHeader)
                {
                    para.ParagraphProperties = new ParagraphProperties(
                        new Justification()
                        {
                            Val = JustificationValues.Left
                        }
                    );

                    run.RunProperties.Bold = new Bold();
                }

                cell.Append(para);
            }

            return cell;
        }
        private void ManageIgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ignoreManager == null)
            {
                MessageBox.Show("Please select a folder first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var window = new IgnoreEditorWindow(_ignoreManager);
            window.ShowDialog();
        }
        
        private void AddToIgnore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is FileSystemNode node)
            {
                var relativePath = Path.GetRelativePath(_rootFolder, node.Path).Replace('\\', '/');
        
                // Đơn giản chỉ dùng relativePath cho cả file và folder
                string pattern = relativePath;
        
                _ignoreManager?.AddRule(pattern);
                _ignoreManager?.SaveRules();
        
                MessageBox.Show($"Added '{pattern}' to ignore list.", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
        
                // Refresh the tree view
                // LoadFileSystem(_rootFolder);
            }
        }
        
        private void RefreshNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is FileSystemNode clickedNode)
            {
                clickedNode.Children.Clear();
                PopulateNode(clickedNode, clickedNode.Path);
            }
        }


        

    }

    // Model for TreeView nodes
    public class FileSystemNode : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Path { get; set; }

        public string Name
        {
            get => System.IO.Path.GetFileName(Path);
        }

        public bool IsDirectory { get; set; }
        public ObservableCollection<FileSystemNode> Children { get; set; } = new ObservableCollection<FileSystemNode>();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                if (IsDirectory)
                {
                    foreach (var child in Children) { child.IsSelected = value; }
                }
            }
        }

        public FileSystemNode(string path, bool isDirectory)
        {
            Path = path;
            IsDirectory = isDirectory;
            _isSelected = true; // Default to selected
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Original data models
    public class ClassInfo
    {
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public bool IsInterface { get; set; } = false;
        public string Summary { get; set; }
        public List<ClassMember> Attributes { get; set; } = new List<ClassMember>();
        public List<MethodMember> Methods { get; set; } = new List<MethodMember>();
    }

    public class ClassMember
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Visibility { get; set; }
        public string Summary { get; set; }
    }

    public class MethodMember
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public string Visibility { get; set; }
        public string Summary { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
    }

    public class ParameterInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Summary { get; set; }

    }
}