%namespace Microsoft.Formula.API
%visibility internal

%x COMMENT
%x QUOTE
%x STRING_SINGLE
%x STRING_MULTI
%s UNQUOTE

%{
		 private Dictionary<string, int> keywords = null;

		 internal int QuotingDepth
		 {
		    get;
		    set;
		 }
		 
		 internal ParseResult ParseResult
		 {
			get;
			set;
		 }

         override public void yyerror(string message, params object[] args)
         {
		   if (ParseResult == null)
		   {
		       throw new Exception("Bad scanner state");
		   }

		   var errFlag = new Flag(
							SeverityKind.Error,
							new Span(yylloc.StartLine, yylloc.StartColumn, yylloc.EndLine, yylloc.StartColumn + yyleng),
							Constants.BadSyntax.ToString(string.Format(message, args)),
							Constants.BadSyntax.Code,
							ParseResult.Program.Node.Name);

		   ParseResult.AddFlag(errFlag);
         }

		 private void MkKeywords()
		 {   
		     if (keywords != null)
			 {
				return;
			 }

			 keywords = new Dictionary<string, int>(28);

			 keywords.Add("domain",    (int)Tokens.DOMAIN);
			 keywords.Add("model",     (int)Tokens.MODEL);
			 keywords.Add("transform", (int)Tokens.TRANSFORM);
			 keywords.Add("system",    (int)Tokens.SYSTEM);

			 keywords.Add("includes", (int)Tokens.INCLUDES);
			 keywords.Add("extends",  (int)Tokens.EXTENDS);
			 keywords.Add("of",       (int)Tokens.OF);
			 keywords.Add("returns",  (int)Tokens.RETURNS);
			 keywords.Add("at",       (int)Tokens.AT);
			 keywords.Add("machine",  (int)Tokens.MACHINE);

			 keywords.Add("is",     (int)Tokens.IS);
			 keywords.Add("no",     (int)Tokens.NO);

			 keywords.Add("new", (int)Tokens.NEW);
			 keywords.Add("fun", (int)Tokens.FUN);
			 keywords.Add("inj", (int)Tokens.INJ);
			 keywords.Add("bij", (int)Tokens.BIJ);
			 keywords.Add("sur", (int)Tokens.SUR);
			 keywords.Add("any", (int)Tokens.ANY);
			 keywords.Add("sub", (int)Tokens.SUB);

			 keywords.Add("ensures", (int)Tokens.ENSURES);
			 keywords.Add("requires", (int)Tokens.REQUIRES);
			 keywords.Add("conforms", (int)Tokens.CONFORMS);
			 keywords.Add("some", (int)Tokens.SOME);
			 keywords.Add("atleast", (int)Tokens.ATLEAST);
			 keywords.Add("atmost", (int)Tokens.ATMOST);
			 keywords.Add("partial", (int)Tokens.PARTIAL);
			 keywords.Add("initially", (int)Tokens.INITIALLY);
			 keywords.Add("next", (int)Tokens.NEXT);
			 keywords.Add("property", (int)Tokens.PROPERTY);
			 keywords.Add("boot", (int)Tokens.BOOT);
		 }

         int GetIdToken(string txt)
         {
		    MkKeywords();

		    int tokId;
			if (keywords.TryGetValue(txt, out tokId))
			{
			   return tokId;
			}

            if (txt.Contains("."))
            {
                if (!txt.EndsWith("."))
                {
                    return (int)Tokens.QUALID;
                }
                else
                {
                    return (int)Tokens.LEX_ERROR;
                }
            }
            else
            {
                return (int)Tokens.BAREID;
            }
		}

       internal void LoadYylval()
       {
            // Trigger lazy evaluation of yytext
            int dummy = yytext.Length;
            
            yylval.str = tokTxt;
            yylloc = new QUT.Gppg.LexLocation(tokLin, tokCol, tokELin, tokECol);
       }
%}

CmntStart       \/\*
CmntEnd         \*\/
CmntStartAlt    \/\/
LF              [\n\r]
NonLF           [^\n\r]*

White0          [ \t\r\f\v]
White           {White0}|\n
NonQCntrChars   [^`$\n\r]*
NonSMCntrChars  [^\'\"\n\r]*

BId             [A-Za-z_]([A-Za-z_0-9]*[']*)
TId             [#]{BId}([\[][0-9]+[\]])?
SId             [\%]{BId}([\.]{BId})*

%%

{CmntStartAlt}{NonLF}{LF}                  { return (int)Tokens.LEX_COMMENT; }
{CmntStartAlt}{NonLF}                      { return (int)Tokens.LEX_COMMENT; }
{CmntStart}                                { BEGIN(COMMENT); return (int)Tokens.LEX_COMMENT; }
<COMMENT>{CmntEnd}                         { if (QuotingDepth == 0) BEGIN(INITIAL); else BEGIN(UNQUOTE); return (int)Tokens.LEX_COMMENT; }
<COMMENT>[.]*{LF}                          { return (int)Tokens.LEX_COMMENT; }
<COMMENT>[.]*                              { return (int)Tokens.LEX_COMMENT; }

({BId}[\.])*({BId}|{TId}|{SId})            { return GetIdToken(yytext);   }
[\-+]?[0-9]+                               { return (int)Tokens.DIGITS;   }
[\-+]?[0-9]+[\.][0-9]+                     { return (int)Tokens.REAL;     }
[\-+]?[0-9]+[\/][\-+]?[0-9]*[1-9]          { return (int)Tokens.FRAC;     }

[\|]                                       { return (int)Tokens.PIPE; }
"::="                                      { return (int)Tokens.TYPEDEF; }
":-"                                       { return (int)Tokens.RULE;    }
"::"                                       { return (int)Tokens.RENAMES; }
".."                                       { return (int)Tokens.RANGE;   }
[\.]                                       { return (int)Tokens.DOT;     }
[:]                                        { return (int)Tokens.COLON;   }

[,]                                        { return (int)Tokens.COMMA;     }
[;]                                        { return (int)Tokens.SEMICOLON; }

[=]                                        { return (int)Tokens.EQ; }
"!="                                       { return (int)Tokens.NE; }
"<="                                       { return (int)Tokens.LE; }
">="                                       { return (int)Tokens.GE; }
[<]                                        { return (int)Tokens.LT; }
[>]                                        { return (int)Tokens.GT; }

[+]                                        { return (int)Tokens.PLUS;  }
[\-]                                       { return (int)Tokens.MINUS; }
[*]                                        { return (int)Tokens.MUL;   }
[\/]                                       { return (int)Tokens.DIV;   }
[%]                                        { return (int)Tokens.MOD;   }

"=>"                                       { return (int)Tokens.STRONGARROW; }
"->"                                       { return (int)Tokens.WEAKARROW;   }

[{]                                        { return (int)Tokens.LCBRACE; }
[}]                                        { return (int)Tokens.RCBRACE; }
[\[]                                       { return (int)Tokens.LSBRACE; }
[\]]                                       { return (int)Tokens.RSBRACE; }
[(]                                        { return (int)Tokens.LPAREN;  }
[)]                                        { return (int)Tokens.RPAREN;  }

[\"]									   { BEGIN(STRING_SINGLE); return (int)Tokens.STRSNGSTART; }
<STRING_SINGLE>{
[^\"\n\r\\]*							   { return (int)Tokens.STRSNG;     }
[\\][^\n\r]								   { return (int)Tokens.STRSNGESC;  }
[\"]								       { if (QuotingDepth == 0) BEGIN(INITIAL); else BEGIN(UNQUOTE); return (int)Tokens.STRSNGEND; }
{LF} | [\\] | <<EOF>>					   { return (int)Tokens.RUNAWAYSTRING; }
}

"\'\""									   { BEGIN(STRING_MULTI); return (int)Tokens.STRMULSTART; }
<STRING_MULTI>{
{NonSMCntrChars}{LF}					   { return (int)Tokens.STRMUL;  }
{NonSMCntrChars}  						   { return (int)Tokens.STRMUL;  }
"\'\'\"\""                                 { return (int)Tokens.STRMULESC; }
"\"\"\'\'"                                 { return (int)Tokens.STRMULESC; }
"\"\'"									   { if (QuotingDepth == 0) BEGIN(INITIAL); else BEGIN(UNQUOTE); return (int)Tokens.STRMULEND; }
[\"\']									   { return (int)Tokens.STRMUL;  }
<<EOF>>					                   { return (int)Tokens.RUNAWAYSTRING; }
}

[`]										   { ++QuotingDepth; BEGIN(QUOTE); return (int)Tokens.QSTART; }
<QUOTE>{
{NonQCntrChars}{LF}					       { return (int)Tokens.QUOTERUN;  }
{NonQCntrChars}  						   { return (int)Tokens.QUOTERUN;  }
"``"									   { return (int)Tokens.QUOTEESCAPE; }
"$$"									   { return (int)Tokens.QUOTEESCAPE; }
[`]										   { --QuotingDepth; if (QuotingDepth == 0) BEGIN(INITIAL); else BEGIN(UNQUOTE); return (int)Tokens.QEND; }
[$]										   { BEGIN(UNQUOTE); return (int)Tokens.UQSTART; }
}

<UNQUOTE>{
[$]										   { BEGIN(QUOTE); return (int)Tokens.UQEND; }
}
                              
['#^´\\§$~]                               { return (int)Tokens.ALIENCHAR;     }

%{
    LoadYylval();
%}

%%
