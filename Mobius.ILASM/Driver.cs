﻿using Mobius.ILasm.infrastructure;
using Mobius.ILasm.interfaces;
using Mono.ILASM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ILAsmException = Mobius.ILasm.infrastructure.ILAsmException;

namespace Mobius.ILasm.Core
{
    //TODO - Search for all TODO references before starting code again. 
    //This repo contains code where references of Report.cs are being removed
    //and instead being changed to either the logger or FileProcessor.
    public class Driver
    {
        private readonly ILogger logger;
        enum Target
        {
            Dll,
            Exe
        }

        public Driver(ILogger logger)
        {
            this.logger = logger; ;
        }

        //public int Assemble(string[] args)
        //{
        //    System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        //    DriverMain driver = new DriverMain(args, logger);
        //    if (!driver.Run())
        //        return 1;
        //    //Report.Message("Operation completed successfully");
        //    logger.Info("Operation completed successfully");
        //    return 0;
        //}


        public int Assemble(string[] args, MemoryStream memoryStream)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            DriverMain driver = new DriverMain(args, logger, memoryStream);
            if (!driver.Run())
                return 1;
            //Report.Message("Operation completed successfully");
            logger.Info("Operation completed successfully");
            return 0;
        }

        public class DriverMain
        {

            private ArrayList il_file_list;
            private string output_file;
            private Target target = Target.Exe;
            private string target_string = "exe";
            private bool show_tokens = false;
            //                        private bool show_method_def = false;
            //                        private bool show_method_ref = false;
            private bool show_parser = false;
            private bool scan_only = false;
            private bool debugging_info = false;
            private CodeGen codegen;
            private bool keycontainer = false;
            private string keyname;
            private readonly ILogger logger;
            private MemoryStream stream;
#if HAS_MONO_SECURITY
    			private StrongName sn;
#endif
            bool noautoinherit;

            public DriverMain(string[] args, ILogger logger, MemoryStream stream)
            {
                this.logger = logger;
                this.stream = stream;
                //can take a list of files to assemble
                il_file_list = new ArrayList();
                ParseArgs(args);
            }

            public bool Run()
            {
                //Call the assembler without any arguments, results in console output of it's usage information
                if (il_file_list.Count == 0)
                    Usage();
                //TODO needs to go as we will be using a Stream instead of a FileStream going forward.
                //if (output_file == null)
                //    output_file = CreateOutputFilename();
                try
                {
                    codegen = new CodeGen(logger, output_file, stream, target == Target.Dll, debugging_info, noautoinherit);
                    foreach (string file_path in il_file_list)
                    {
                        //The filepath needs to go as we will be using stream
                        //but we need a mechanism to keep information about every stream
                        FileProcessor.FilePath = file_path;
                        ProcessFile(file_path, stream);
                    }
                    if (scan_only)
                        return true;

                    if (FileProcessor.ErrorCount > 0)
                        return false;

                    if (target != Target.Dll && !codegen.HasEntryPoint)
                    {
                        logger.Error("No entry point found.");
                        FileProcessor.ErrorCount += 1;
                    }

                    // if we have a key and aren't assembling a netmodule
                    if ((keyname != null) && !codegen.IsThisAssembly(null))
                    {
#if HAS_MONO_SECURITY
    						LoadKey ();
    						// this overrides any attribute or .publickey directive in the source
    						codegen.ThisAssembly.SetPublicKey (sn.PublicKey);
#else
                        throw new NotSupportedException();
#endif
                    }

                    try
                    {
                        codegen.Write();
                    }
                    catch
                    {
                        File.Delete(output_file);
                        throw;
                    }
                }
                catch (ILAsmException e)
                {
                    logger.Error(e.ToString());
                    return false;
                }
                catch (PEAPI.PEFileException pe)
                {
                    logger.Error("Error : " + pe.Message);
                    return false;
                }

#if HAS_MONO_SECURITY
                                    try {
    					if (sn != null) {
    						Report.Message ("Signing assembly with the specified strongname keypair");
    						return Sign (output_file);
    					}
                                    } catch {
                                            return false;
                                    }
#endif

                return true;
            }

#if HAS_MONO_SECURITY
    			private void LoadKey ()
    			{
    				if (keycontainer) {
    					CspParameters csp = new CspParameters ();
    					csp.KeyContainerName = keyname;
    					RSACryptoServiceProvider rsa = new RSACryptoServiceProvider (csp);
    					sn = new StrongName (rsa);
    				} else {
    					byte[] data = null;
    					using (FileStream fs = File.OpenRead (keyname)) {
    						data = new byte [fs.Length];
    						fs.Read (data, 0, data.Length);
    						fs.Close ();
    					}
    					sn = new StrongName (data);
    				}
    			}

    			private bool Sign (string filename)
    			{
    				// note: if the file cannot be signed (no public key in it) then
    				// we do not show an error, or a warning, if the key file doesn't 
    				// exists
    				return sn.Sign (filename);
    			}
#endif

            private void ProcessFile(string file_path, MemoryStream stream)
            {
                if (stream == null)
                {
                    logger.Error($"Stream is empty!");
                    Environment.Exit(2);
                }
                //TODO figure out how to log with the correct IL input filename
                //logger.Info($"Assembling '{file_path}' , {FileProcessor.GetListing(null)}, to {target_string} --> '{output_file}'");
                StreamReader reader = File.OpenText(file_path);
                ILTokenizer scanner = new ILTokenizer(reader);

                if (show_tokens)
                    scanner.NewTokenEvent += new NewTokenEvent(ShowToken);
                //if (show_method_def)
                //        MethodTable.MethodDefinedEvent += new MethodDefinedEvent (ShowMethodDef);
                //if (show_method_ref)
                //       MethodTable.MethodReferencedEvent += new MethodReferencedEvent (ShowMethodRef);

                if (scan_only)
                {
                    ILToken tok;
                    while ((tok = scanner.NextToken) != ILToken.EOF)
                    {
                        logger.Info(tok.ToString());
                    }
                    return;
                }


                ILParser parser = new ILParser(codegen, scanner, this.logger);
                codegen.BeginSourceFile(file_path);
                try
                {
                    if (show_parser)
                        parser.yyparse(new ScannerAdapter(scanner),
                                        new Mono.ILASM.yydebug.yyDebugSimple());
                    else
                        parser.yyparse(new ScannerAdapter(scanner), null);
                }
                catch (ILTokenizingException ilte)
                {
                    logger.Error(ilte.Location, "syntax error at token '" + ilte.Token + "'");
                }
                catch (Mono.ILASM.yyParser.yyException ye)
                {
                    logger.Error(scanner.Reader.Location, ye.Message);
                }
                catch (ILAsmException ie)
                {
                    ie.FilePath = file_path;
                    ie.Location = scanner.Reader.Location;
                    throw;
                }
                catch (Exception)
                {
                    Console.Write("{0} ({1}, {2}): ", file_path, scanner.Reader.Location.line, scanner.Reader.Location.column);
                    throw;
                }
                finally
                {
                    codegen.EndSourceFile();
                }
            }

            public void ShowToken(object sender, NewTokenEventArgs args)
            {
                Console.WriteLine("token: '{0}'", args.Token);
            }
            /*
            public void ShowMethodDef (object sender, MethodDefinedEventArgs args)
            {
                    Console.WriteLine ("***** Method defined *****");
                    Console.WriteLine ("-- signature:   {0}", args.Signature);
                    Console.WriteLine ("-- name:        {0}", args.Name);
                    Console.WriteLine ("-- return type: {0}", args.ReturnType);
                    Console.WriteLine ("-- is in table: {0}", args.IsInTable);
                    Console.WriteLine ("-- method atts: {0}", args.MethodAttributes);
                    Console.WriteLine ("-- impl atts:   {0}", args.ImplAttributes);
                    Console.WriteLine ("-- call conv:   {0}", args.CallConv);
            }

            public void ShowMethodRef (object sender, MethodReferencedEventArgs args)
            {
                    Console.WriteLine ("***** Method referenced *****");
                    Console.WriteLine ("-- signature:   {0}", args.Signature);
                    Console.WriteLine ("-- name:        {0}", args.Name);
                    Console.WriteLine ("-- return type: {0}", args.ReturnType);
                    Console.WriteLine ("-- is in table: {0}", args.IsInTable);
            }
            */
            private void ParseArgs(string[] args)
            {
                string command_arg;
                //Processes a list of .il files to assemble as iterates over every string object in the input array
                foreach (string str in args)
                {
                    //default case where only the path of the .il file is given.
                    if ((str[0] != '-') && (str[0] != '/'))
                    {
                        il_file_list.Add(str);
                        continue;
                    }
                    switch (GetCommand(str, out command_arg))
                    {
                        case "out":
                        case "output":
                            output_file = command_arg;
                            break;
                        case "exe":
                            target = Target.Exe;
                            target_string = "exe";
                            break;
                        case "dll":
                            target = Target.Dll;
                            target_string = "dll";
                            break;
                        //This option must be removed as earlier this was used to suppress any logging via the Report.cs
                        //case "quiet":
                        //    Report.Quiet = true;
                        //    break;
                        case "debug":
                        case "deb":
                            debugging_info = true;
                            break;
                        // Stubs to stay commandline compatible with MS 
                        case "listing":
                        case "nologo":
                        case "clock":
                        case "error":
                        case "subsystem":
                        case "flags":
                        case "alignment":
                        case "base":
                        case "resource":
                            break;
                        case "key":
                            if (command_arg.Length > 0)
                                keycontainer = (command_arg[0] == '@');
                            if (keycontainer)
                                keyname = command_arg.Substring(1);
                            else
                                keyname = command_arg;
                            break;
                        case "noautoinherit":
                            noautoinherit = true;
                            break;
                        case "scan_only":
                            scan_only = true;
                            break;
                        case "show_tokens":
                            show_tokens = true;
                            break;
                        case "show_method_def":
                            //                                                show_method_def = true;
                            break;
                        case "show_method_ref":
                            //                                                show_method_ref = true;
                            break;
                        case "show_parser":
                            show_parser = true;
                            break;
                        case "-about":
                            if (str[0] != '-')
                                break;
                            About();
                            break;
                        case "-version":
                            if (str[0] != '-')
                                break;
                            Version();
                            break;
                        default:
                            if (str[0] == '-')
                                break;
                            il_file_list.Add(str);
                            break;
                    }
                }
            }

            private string GetCommand(string str, out string command_arg)
            {
                int end_index = str.IndexOfAny(new char[] { ':', '=' }, 1);
                string command = str.Substring(1,
                        end_index == -1 ? str.Length - 1 : end_index - 1);

                if (end_index != -1)
                {
                    command_arg = str.Substring(end_index + 1);
                }
                else
                {
                    command_arg = null;
                }

                return command.ToLower();
            }

            /// <summary>
            ///   Get the first file name and makes it into an output file name
            /// </summary>
            private string CreateOutputFilename()
            {
                string file_name = (string)il_file_list[0];
                int ext_index = file_name.LastIndexOf('.');

                if (ext_index == -1)
                    ext_index = file_name.Length;

                return String.Format("{0}.{1}", file_name.Substring(0, ext_index),
                        target_string);
            }

            private void Usage()
            {
                Console.WriteLine("Mono IL assembler compiler\n" +
                        "ilasm [options] source-files\n" +
                        "   --about            About the Mono IL assembler compiler\n" +
                        "   --version          Print the version number of the compiler\n" +
                        "   /output:file_name  Specifies output file.\n" +
                        "   /exe               Compile to executable.\n" +
                        "   /dll               Compile to library.\n" +
                        "   /debug             Include debug information.\n" +
    "   /key:keyfile       Strongname using the specified key file\n" +
    "   /key:@container    Strongname using the specified key container\n" +
                        "   /noautoinherit     Disable inheriting from System.Object by default\n" +
                        "Options can be of the form -option or /option\n");
                Environment.Exit(1);
            }

            private void About()
            {
                Console.WriteLine(
                        "For more information on Mono, visit the project Web site\n" +
                        "   http://www.mono-project.com\n\n");
                Environment.Exit(0);
            }

            private void Version()
            {
                string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                Console.WriteLine("Mono IL assembler compiler version {0}", version);
                Environment.Exit(0);
            }

            private void AssembleFile(string file, string listing,
                                  string target, string output)
            {
                logger.Info($"Assembling '{file}' , {GetListing(listing)}, to {target} --> '{output}'");
            }

            private static string GetListing(string listing)
            {
                if (listing == null)
                    return "no listing file";
                return listing;
            }

        }
    }


}
