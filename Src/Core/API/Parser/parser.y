%namespace Microsoft.Formula.API
%visibility internal

%YYSTYPE LexValue
%partial

%union {
	public string str;
}

%token DOMAIN MODEL TRANSFORM SYSTEM MACHINE PARTIAL
%token ENSURES REQUIRES CONFORMS
%token LCBRACE RCBRACE
%token LPAREN RPAREN
%token LSBRACE RSBRACE

%token INCLUDES EXTENDS OF RETURNS
%token AT COLON
%token RENAMES RANGE 
%token SOME ATLEAST ATMOST INITIALLY NEXT PROPERTY BOOT

%token EQ TYPEDEF RULE PIPE
%token DOT
%token SEMICOLON
%token COMMA
 
%token NO IS WEAKARROW STRONGARROW
%token NEW INJ BIJ SUR FUN ANY SUB
%token BAREID QUALID DIGITS REAL FRAC
%token STRSNGSTART STRSNG STRSNGESC STRSNGEND
%token STRMULSTART STRMUL STRMULESC STRMULEND
%token NE LT GT GE LE

%left  PLUS MINUS
%left  MOD
%left  DIV
%left  MUL 
%left  UMINUS

%token QSTART QEND UQSTART UQEND QUOTERUN QUOTEESCAPE

%token RUNAWAYSTRING 
%token ALIENCHAR
%token maxParseToken 
%token LEX_WHITE LEX_COMMENT LEX_ERROR

%%

Program
   : EOF
   | Config      
   | ModuleList   
   | Config     
	 ModuleList
   ;    

ModuleList
   : Module      { EndModule(); }
   | Module      { EndModule(); }
	 ModuleList
   ;

Module 
   : Domain
   | Model
   | Transform
   | TSystem
   | Machine
   ;

/**************  Machines ************/

Machine
	: MachineSigConfig  
	  LCBRACE			
	  RCBRACE			

	| MachineSigConfig  
	  LCBRACE			{ SetModRefState(ModRefState.ModApply); }
	  MachineBody
	  RCBRACE			
   ;

MachineBody
	: MachineSentenceConf	 
	| MachineSentenceConf
	  MachineBody
	;

MachineSentenceConf
    : MachineSentence
	| SentenceConfig
	  MachineSentence
    ;

MachineSentence
	: MachineProp
	| BOOT        { IsBuildingUpdate = false; } 
	  Step
	| INITIALLY   { IsBuildingNext = false; IsBuildingUpdate = true; }
	  Update
	| NEXT        { IsBuildingNext = true; IsBuildingUpdate = true;  }
	  Update
	;
	  
MachineProp
	: PROPERTY
	  BAREID
	  EQ
	  FuncTerm
	  DOT        { AppendProperty($2.str, ToSpan(@1)); }
	;	

/**************  Machine Signature ************/

MachineSigConfig
   : MachineSig    { SetModRefState(ModRefState.None);  }
   | MachineSig    { SetModRefState(ModRefState.None);  } 
	 Config        
   ;

MachineSig
	: MACHINE
	  BAREID         { StartMachine($2.str, ToSpan(@2)); }
	  MachineSigIn	   
	  OF			 { SetModRefState(ModRefState.Other); }
	  ModRefs       
	;

MachineSigIn
	: LPAREN
	  RPAREN
	| LPAREN
	  VoMParamList
	  RPAREN
	;
			
/**************  Models ************/

Model
   : ModelSigConfig
	 LCBRACE	  
	 RCBRACE     

   | ModelSigConfig
	 LCBRACE      
	 ModelBody
	 RCBRACE   
   ;

ModelBody
   : ModelSentence
   | ModelSentence
	 ModelBody
   ;

ModelSentence
	 : ModelFactList
	 | ModelContractConf
	 ;

ModelContractConf
     : ModelContract
	 | SentenceConfig
	   ModelContract
	 ;

ModelContract
	 : ENSURES    { StartPropContract(ContractKind.EnsuresProp, ToSpan(@1)); }
	   BodyList
	   DOT	       

	 | REQUIRES   { StartPropContract(ContractKind.RequiresProp, ToSpan(@1));  }
	   BodyList
	   DOT		   

	 | REQUIRES
	   CardSpec
	   DIGITS
	   Id          { AppendCardContract($2.str, ParseInt($3.str, ToSpan(@3)), ToSpan(@1)); } 
	   DOT
	 ;

ModelFactList
	   : ModelFact   
		 DOT

       | SentenceConfig
	     ModelFact   
		 DOT

	   | ModelFact   
		 COMMA
		 ModelFactList   

	   | SentenceConfig
	     ModelFact   
		 COMMA
		 ModelFactList   
	   ; 

ModelFact 
	   : FuncTerm    { AppendFact(MkFact(false, ToSpan(@1))); }
	   | BAREID      
		 IS          { PushArg(new Nodes.Id(ToSpan(@1), $1.str));  }
		 FuncTerm    { AppendFact(MkFact(true, ToSpan(@1))); }
	   ;

CardSpec
	 : SOME
	 | ATMOST
	 | ATLEAST
	 ;

/**************  Model Signatures  ************/

ModelSigConfig
   : ModelSig			 { SetModRefState(ModRefState.None); }
   | ModelSig			 { SetModRefState(ModRefState.None); }
	 Config             
   ;

ModelSig
   : ModelIntro
   | ModelIntro
     INCLUDES  { SetCompose(ComposeKind.Includes); SetModRefState(ModRefState.Input); }
	 ModRefs
   | ModelIntro
     EXTENDS  { SetCompose(ComposeKind.Extends); SetModRefState(ModRefState.Input); }
	 ModRefs                        
   ;

ModelIntro
   : MODEL     
	 BAREID    { StartModel($2.str, false, ToSpan(@1)); }
	 OF
	 ModRef    

   | PARTIAL   
	 MODEL     
	 BAREID    { StartModel($3.str, true, ToSpan(@1)); }
	 OF
	 ModRef    
   ;

/**************  Transform Systems  ************/

TSystem 
	   : TRANSFORM
		 SYSTEM
	     BAREID      { StartTSystem($3.str, ToSpan(@1)); }
		 TSystemRest
	   ;

TSystemRest 
	   : TransformSigConfig
		 LCBRACE
		 RCBRACE 

	   | TransformSigConfig
		 LCBRACE     { IsBuildingUpdate = false; SetModRefState(ModRefState.ModApply); }
		 TransSteps
		 RCBRACE  
	   ;	

TransSteps
	 : TransStepConfig
	 | TransStepConfig
	   TransSteps
	 ; 

TransStepConfig
     : Step
	 | SentenceConfig
	   Step
     ;

/**************  Transforms  ************/

Transform 
	   : TRANSFORM
	     BAREID      { StartTransform($2.str, ToSpan(@1)); }
		 TransformRest
	   ;

TransformRest
	   : TransformSigConfig
		 LCBRACE
		 RCBRACE

	   | TransformSigConfig
		 LCBRACE     
		 TransBody
		 RCBRACE  
	   ;	

TransBody
	 : TransSentenceConfig
	 | TransSentenceConfig
	   TransBody
	 ;

TransSentenceConfig
     : TransSentence
	 | SentenceConfig
	   TransSentence
	 ;

TransSentence
	 : Rule
	 | TypeDecl	 
	 | ENSURES    { StartPropContract(ContractKind.EnsuresProp, ToSpan(@1)); }
	   BodyList
	   DOT		  
	 | REQUIRES   { StartPropContract(ContractKind.RequiresProp, ToSpan(@1)); }
	   BodyList
	   DOT	      
	 ;

/**************  Transform Signatures ************/

TransformSigConfig
		: TransformSig   { SetModRefState(ModRefState.None);  }
		| TransformSig   { SetModRefState(ModRefState.None);  }
		  Config         
		;

TransformSig
		: TransSigIn		  
		  RETURNS       		  
		  LPAREN         { SetModRefState(ModRefState.Output); }
		  ModelParamList
		  RPAREN         
		;
 
TransSigIn
		: LPAREN
		  RPAREN
		| LPAREN
		  VoMParamList
		  RPAREN
		;

/**************  Domains  ************/

Domain 
	   : DomainSigConfig
		 LCBRACE
		 RCBRACE 

	   | DomainSigConfig  
		 LCBRACE
		 DomSentences
		 RCBRACE   
	   ;

DomSentences 
	 : DomSentenceConfig   
	 | DomSentenceConfig
	   DomSentences   
	 ;

DomSentenceConfig
	 : DomSentence
	 | SentenceConfig
	   DomSentence
	 ;

DomSentence 
	 : Rule
	 | TypeDecl
	 | CONFORMS { StartPropContract(ContractKind.ConformsProp, ToSpan(@1)); }
	   BodyList
	   DOT		  
	 ;

/*************** Domain Signature ***************/

DomainSigConfig
		: DomainSig  { SetModRefState(ModRefState.None);  }
		| DomainSig  { SetModRefState(ModRefState.None);  }
		  Config     
		;

DomainSig 
		: DOMAIN
		  BAREID     { StartDomain($2.str, ComposeKind.None, ToSpan(@1)); }

		| DOMAIN
		  BAREID
		  EXTENDS    { StartDomain($2.str, ComposeKind.Extends, ToSpan(@1)); }
		  ModRefs    

		| DOMAIN
		  BAREID
		  INCLUDES  { StartDomain($2.str, ComposeKind.Includes, ToSpan(@1)); }
		  ModRefs       
		;

/**************  Configurations ************/

Config
	   : LSBRACE
		 SettingList
		 RSBRACE
	   ;

SentenceConfig
	   : LSBRACE       { StartSentenceConfig(ToSpan(@1)); }
		 SettingList
		 RSBRACE
	   ;

SettingList
	   : Setting        
	   | Setting
		 COMMA
		 SettingList        
	   ;

Setting 
	   : Id
		 EQ
		 Constant  { AppendSetting(); }
	   ;

/**************  Parameters ************/

ModelParamList
	   : ModRefRename
	   | ModRefRename
		 COMMA
		 ModelParamList
	   ;

ValOrModelParam 
	   : BAREID
		 COLON
		 UnnBody         { AppendParam($1.str, ToSpan(@1)); }
	   | ModRefRename
	   ;

VoMParamList
	   : ValOrModelParam
	   | ValOrModelParam
		 COMMA
		 VoMParamList
	   ;

/**************  Steps and Updates ************/

Update
	 : StepOrUpdateLHS
	   EQ
	   ChoiceList
	   DOT			 { AppendUpdate(); }
	 ;

Step
	 : StepOrUpdateLHS
	   EQ
	   ModApply		 
	   DOT			 { AppendStep(); }
	 ;

ChoiceList 
	: ModApply       { AppendChoice(); }
	| ModApply       { AppendChoice(); }
	  SEMICOLON
	  ChoiceList
	;

ModApply
	 : ModRef
	   LPAREN         
	   RPAREN         { PushArg(MkModApply()); }

	 | ModRef
	   LPAREN         
	   ModArgList
	   RPAREN         { PushArg(MkModApply()); }
	 ;

ModArgList
	: ModAppArg         { IncArity(); }
	| ModAppArg         { IncArity(); }
	  COMMA
	  ModArgList
	;

ModAppArg 
	: FuncTerm   
    | BAREID
      AT
      String          { PushArg(new Nodes.ModRef(ToSpan(@1), $1.str, null, GetStringValue())); }
    ;

StepOrUpdateLHS 
	 : Id			  { AppendLHS(); }
	 | Id			  { AppendLHS(); }
	   COMMA
	   StepOrUpdateLHS
	 ;

/**************  Module References ************/

ModRefs 
		: ModRef
		| ModRef
		  COMMA
		  ModRefs
		;

ModRef 
		: ModRefRename
		| ModRefNoRename
		;

ModRefRename 
	   : BAREID
		 RENAMES
		 BAREID    { AppendModRef(new Nodes.ModRef(ToSpan(@1), $3.str, $1.str, null)); }
		   
	   | BAREID
		 RENAMES
		 BAREID
		 AT
		 String    { AppendModRef(new Nodes.ModRef(ToSpan(@1), $3.str, $1.str, GetStringValue())); }
	   ;

ModRefNoRename 
	   : BAREID    { AppendModRef(new Nodes.ModRef(ToSpan(@1), $1.str, null, null)); }
	   | BAREID
		 AT
		 String    { AppendModRef(new Nodes.ModRef(ToSpan(@1), $1.str, null, GetStringValue())); }
	   ;

/**************** Type Decls *****************/

TypeDecl 
		 : BAREID        { SaveTypeDeclName($1.str, ToSpan(@1)); }
		   TYPEDEF      
		   TypeDeclBody
		   DOT          
		 ;

TypeDeclBody 
        : UnnBody        { EndUnnDecl(); } 

		| LPAREN		 { StartConDecl(false, false); } 
		  Fields
		  RPAREN         { EndTypeDecl(); }

		| SUB	         { StartConDecl(false, true); }
		  LPAREN
		  Fields
		  RPAREN         { EndTypeDecl(); }

		| NEW	         { StartConDecl(true, false); }
		  LPAREN
		  Fields
		  RPAREN         { EndTypeDecl(); }

		| FunDecl        
		  LPAREN
		  Fields
		  MapArrow       
		  Fields
		  RPAREN         { EndTypeDecl(); }
		;		 

FunDecl 
		: INJ          { StartMapDecl(MapKind.Inj); }
		| BIJ		   { StartMapDecl(MapKind.Bij); }
		| SUR		   { StartMapDecl(MapKind.Sur); }
		| FUN          { StartMapDecl(MapKind.Fun); }
		;

Fields 
	   : Field
	   | Field
		 COMMA
		 Fields
	   ;

Field 
	  : UnnBody         { AppendField(null, false, ToSpan(@1)); }
	  | ANY            
		UnnBody         { AppendField(null, true, ToSpan(@1)); }
	  | BAREID
		COLON
		UnnBody         { AppendField($1.str, false, ToSpan(@1)); }
	  | BAREID
		COLON
		ANY
		UnnBody         { AppendField($1.str, true, ToSpan(@1)); }
	  ;

MapArrow 
      : WEAKARROW       { SaveMapPartiality(true); }
	  | STRONGARROW     { SaveMapPartiality(false); }
	  ;

/**************** Type Terms *****************/

UnnBody 
		: UnnCmp
		| UnnCmp
		  PLUS
		  UnnBody
		;

UnnCmp 
	   : TypeId
	   | LCBRACE    { StartEnum(ToSpan(@1)); }
		 EnumList
		 RCBRACE    { EndEnum(); }
	   ;

TypeId
	 : BAREID         { AppendUnion(new Nodes.Id(ToSpan(@1), $1.str)); }
	 | QUALID         { AppendUnion(new Nodes.Id(ToSpan(@1), $1.str)); }
	 ; 

EnumList 
		 : EnumCnst
		 | EnumCnst
		   COMMA
		   EnumList
		 ;

EnumCnst
		 : DIGITS      { AppendEnum(ParseNumeric($1.str, false, ToSpan(@1))); }
		 | REAL        { AppendEnum(ParseNumeric($1.str, false, ToSpan(@1)));    }
		 | FRAC        { AppendEnum(ParseNumeric($1.str, true,  ToSpan(@1)));    }
		 | String      { AppendEnum(GetString());  }
		 | BAREID      { AppendEnum(new Nodes.Id(ToSpan(@1), $1.str));  }
		 | QUALID      { AppendEnum(new Nodes.Id(ToSpan(@1), $1.str));  }
		 | DIGITS      
		   RANGE
		   DIGITS      { AppendEnum(new Nodes.Range(ToSpan(@1), ParseNumeric($1.str), ParseNumeric($3.str))); }
		 ;

/************* Facts, Rules, and Comprehensions **************/

Rule 
	 : FuncTermList       { EndHeads(ToSpan(@1));   }
	   DOT                { AppendRule(); }
	 | FuncTermList
	   RULE               { EndHeads(ToSpan(@1));  }
	   BodyList
	   DOT				  { AppendRule(); }
	 ;

Compr  
	 : LCBRACE		      { PushComprSymbol(ToSpan(@1)); } 
	   FuncTermList                  
	   ComprRest
	 ;

ComprRest  
	 : RCBRACE			  { EndComprHeads(); PushArg(MkCompr()); }
	 | PIPE				  { EndComprHeads(); }
	   BodyList 
	   RCBRACE			  { PushArg(MkCompr()); }
	 ;

BodyList 
	: Body				  { AppendBody(); }      
	| Body			      { AppendBody(); }     
	  SEMICOLON   
	  BodyList
	;

Body 
	: Constraint          
	| Constraint
	  COMMA
	  Body
	;

/******************* Terms and Constraints *******************/

Constraint
	: FuncTerm		      { AppendConstraint(MkFind(false, ToSpan(@1))); }

	| Id
	  IS
	  FuncTerm		      { AppendConstraint(MkFind(true, ToSpan(@1))); }

	| NO 
	  Compr               { AppendConstraint(MkNoConstr(ToSpan(@1))); }

	| NO 
	  FuncTerm		      { AppendConstraint(MkNoConstr(ToSpan(@1), false)); }

	| NO 
	  Id
	  IS
	  FuncTerm		      { AppendConstraint(MkNoConstr(ToSpan(@1), true)); } 

	| FuncTerm
	  RelOp
	  FuncTerm       { AppendConstraint(MkRelConstr()); }
	;

FuncTermList
	: FuncOrCompr         { IncArity(); }
	| FuncOrCompr         { IncArity(); }
	  COMMA
	  FuncTermList
	;

FuncOrCompr
	: FuncTerm
	| Compr				  
	;

FuncTerm 
	: Atom
		
	| UnOp
	  FuncTerm %prec UMINUS { PushArg(MkTerm(1)); }

	| FuncTerm				  
	  BinOp
	  FuncTerm            { PushArg(MkTerm(2)); }

	| Id				 
	  LPAREN              { PushSymbol(); }
	  FuncTermList   
	  RPAREN			  { PushArg(MkTerm()); }
	  
	| QSTART              { PushQuote(ToSpan(@1)); }
	  QuoteList
	  QEND                { PushArg(PopQuote());   }
	   
	| LPAREN
	  FuncTerm   
	  RPAREN			
	;

QuoteList
	 : QuoteItem
	 | QuoteItem
	   QuoteList
	 ;

QuoteItem
	 : QUOTERUN    { AppendQuoteRun($1.str, ToSpan(@1)); }
	 | QUOTEESCAPE { AppendQuoteEscape($1.str, ToSpan(@1)); }
	 | UQSTART
	   FuncTerm
	   UQEND	   { AppendUnquote(); }
	 ;

Atom 
	 : Id
	 | Constant
	 ;

Id
	 : BAREID        { PushArg(new Nodes.Id(ToSpan(@1), $1.str));  }
	 | QUALID        { PushArg(new Nodes.Id(ToSpan(@1), $1.str));  }
	 ;
	 
Constant 
	 : DIGITS      { PushArg(ParseNumeric($1.str, false, ToSpan(@1))); }
	 | REAL        { PushArg(ParseNumeric($1.str, false, ToSpan(@1))); }
	 | FRAC        { PushArg(ParseNumeric($1.str, true,  ToSpan(@1))); }
	 | String      { PushArg(GetString()); }
	 ;
	 
UnOp 
	 : MINUS       { PushSymbol(OpKind.Neg,   ToSpan(@1));  }
	 ;
 
BinOp
	   : MUL         { PushSymbol(OpKind.Mul,  ToSpan(@1));  }
	   | DIV         { PushSymbol(OpKind.Div,  ToSpan(@1));  }
	   | MOD         { PushSymbol(OpKind.Mod,  ToSpan(@1));  }
	   | PLUS        { PushSymbol(OpKind.Add,  ToSpan(@1));  }
	   | MINUS	     { PushSymbol(OpKind.Sub,  ToSpan(@1));  }
	   ;   

RelOp : EQ           { PushSymbol(RelKind.Eq,  ToSpan(@1));  }
	  | NE           { PushSymbol(RelKind.Neq, ToSpan(@1));  }
	  | LT           { PushSymbol(RelKind.Lt,  ToSpan(@1));  }
	  | LE           { PushSymbol(RelKind.Le,  ToSpan(@1));  }
	  | GT           { PushSymbol(RelKind.Gt,  ToSpan(@1));  }
	  | GE           { PushSymbol(RelKind.Ge,  ToSpan(@1));  }
	  | COLON        { PushSymbol(RelKind.Typ, ToSpan(@1));  }
	  ;

String : StrStart
		 StrBodyList
		 StrEnd
	   | StrStart
		 StrEnd
	   ;

StrStart : STRSNGSTART { StartString(ToSpan(@1)); }
         | STRMULSTART { StartString(ToSpan(@1)); }
		 ;

StrBodyList 
        : StrBody
		| StrBody
		  StrBodyList
		;

StrBody : STRSNG     { AppendString($1.str); }
	    | STRSNGESC  { AppendSingleEscape($1.str); }
        | STRMUL     { AppendString($1.str); }
		| STRMULESC  { AppendMultiEscape($1.str); }
		;

StrEnd  : STRSNGEND { EndString(ToSpan(@1)); }
        | STRMULEND { EndString(ToSpan(@1)); }
		;
%%
