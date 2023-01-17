parser grammar FormulaParser;
options { tokenVocab=FormulaLexer; }

program				:	EOF
					|	config
					|	moduleList
					|	config
						moduleList
					;

moduleList			:	module+
					;

module				:	domain
					|	model
					|	transform
					|	tSystem
					|	machine
					;

// Machines

machine				:	machineSigConfig
						LCBRACE
						RCBRACE
					|	machineSigConfig
						LCBRACE
						machineBody
						RCBRACE
					;

machineBody			:	machineSentenceConf
					|	machineSentenceConf
						machineBody
					;

machineSentenceConf	:	machineSentence
					|	sentenceConfig
						machineSentence
					;

machineSentence		:	machineProp
					|	BOOT
						step
					|	INITIALLY
						update
					|	NEXT
						update
					;

machineProp			:	PROPERTY
						BAREID
						EQ
						funcTerm
						DOT
					;

// Machine Signature

machineSigConfig	:	machineSig
					|	machineSig
						config
					;

machineSig			:	MACHINE
						BAREID
						machineSigIn
						OF
						modRefs
					;

machineSigIn		:	LPAREN
						RPAREN
					|	LPAREN
						vomParamList
						RPAREN
					;

// Models

model				:	modelSigConfig
						LCBRACE
						modelBody?
						RCBRACE
					;

modelBody			:	modelSentence+
					;

modelSentence		:	modelFactList
					|	modelContractConf
					;

modelContractConf	:	modelContract
					|	sentenceConfig
						modelContract
					;

modelContract		:	ENSURES
						bodyList
						DOT
					|	REQUIRES
						bodyList
						DOT
					|	REQUIRES
						cardSpec
						DIGITS
						id
						DOT
					;

modelFactList		:	sentenceConfig?
						modelFact
						( COMMA modelFactList
						|
						  DOT
						)
					;

modelFact			:	(BAREID IS)?
						funcTerm
					;

cardSpec			:	SOME
					|	ATMOST
					|	ATLEAST
					;

// Model Signatures

modelSigConfig		:	modelSig
					|	modelSig
						config
					;

modelSig			:	modelIntro
					|	modelIntro
						INCLUDES
						modRefs
					|	modelIntro
						EXTENDS
						modRefs
					;

modelIntro			:	MODEL
						BAREID
						OF
						modRef
					|	PARTIAL
						MODEL
						BAREID
						OF
						modRef
					;

// Transform Systems

tSystem				:	TRANSFORM
						SYSTEM
						BAREID
						tSystemRest
					;

tSystemRest			:	transformSigConfig
						LCBRACE
						RCBRACE
					|	transformSigConfig
						LCBRACE
						transSteps
						RCBRACE
					;

transSteps			:	transStepConfig
					|	transStepConfig
						transSteps
					;

transStepConfig		:	step
					|	sentenceConfig
						step
					;

// Transforms

transform			:	TRANSFORM
						BAREID
						transformRest
					;

transformRest		:	transformSigConfig
						LCBRACE
						RCBRACE
					|	transformSigConfig
						LCBRACE
						transBody
						RCBRACE
					;

transBody			:	transSentenceConfig
					|	transSentenceConfig
						transBody
					;

transSentenceConfig	:	transSentence
					|	sentenceConfig
						transSentence
					;

transSentence		:	ruleItem
					|	typeDecl
					|	ENSURES
						bodyList
						DOT
					|	REQUIRES
						bodyList
						DOT
					;

// Transform Signatures

transformSigConfig	:	transformSig
					|	transformSig
						config
					;

transformSig		:	transSigIn
						RETURNS
						LPAREN
						modelParamList
						RPAREN
					;

transSigIn			:	LPAREN
						RPAREN
					|	LPAREN
						vomParamList
						RPAREN
					;

// Domains

domain				:	domainSigConfig
						LCBRACE
						RCBRACE
					|	domainSigConfig
						LCBRACE
						domSentences
						RCBRACE
					;

domSentences		:	domSentenceConfig
					|	domSentenceConfig
						domSentences
					;

domSentenceConfig	:	domSentence
					|	sentenceConfig
						domSentence
					;

domSentence			:	ruleItem
					|	typeDecl
					|	CONFORMS
						bodyList
						DOT
					;

// Domain Signature

domainSigConfig		:	domainSig
					|	domainSig
						config
					;

domainSig			:	DOMAIN
						BAREID
					|	DOMAIN
						BAREID
						EXTENDS
						modRefs
					|	DOMAIN
						BAREID
						INCLUDES
						modRefs
					;

// Configurations

config				:	LSBRACE
						settingList
						RSBRACE
					;

sentenceConfig		:	LSBRACE
						settingList
						RSBRACE
					;

settingList			:	setting
					|	setting
						COMMA
						settingList
					;

setting				:	id
						EQ
						constant
					;

// Parameters

modelParamList		:	modRefRename
					|	modRefRename
						COMMA
						modelParamList
					;

valOrModelParam		:	BAREID
						COLON
						unnBody
					|	modRefRename
					;

vomParamList		:	valOrModelParam
					|	valOrModelParam
						COMMA
						vomParamList
					;

// Steps and Updates

update				:	stepOrUpdateLHS
						EQ
						choiceList
						DOT
					;

step				:	stepOrUpdateLHS
						EQ
						modApply
						DOT
					;

choiceList			:	modApply
					|	modApply
						SEMICOLON
						choiceList
					;

modApply			:	modRef
						LPAREN
						RPAREN
					|	modRef
						LPAREN
						modArgList
						RPAREN
					;

modArgList			:	modAppArg
					|	modAppArg
						COMMA
						modArgList
					;

modAppArg			:	funcTerm
					|	BAREID
						AT
						str
					;

stepOrUpdateLHS		:	id
					|	id
						COMMA
						stepOrUpdateLHS
					;

// Module References

modRefs				:	modRef
					|	modRef
						COMMA
						modRefs
					;

modRef				:	modRefRename
					|	modRefNoRename
					;

modRefRename		:	BAREID
						RENAMES
						BAREID
					|	BAREID
						RENAMES
						BAREID
						AT
						str
					;

modRefNoRename		:	BAREID
					|	BAREID
						AT
						str
					;

// Type Decls

typeDecl			:	BAREID
						TYPEDEF
						typeDeclBody
						DOT
					;

typeDeclBody		:	unnBody
					|	LPAREN
						fields
						RPAREN
					|	SUB
						LPAREN
						fields
						RPAREN
					|	NEW
						LPAREN
						fields
						RPAREN
					|	funDecl
						LPAREN
						fields
						mapArrow
						fields
						RPAREN
					;

funDecl				:	INJ
					|	BIJ
					|	SUR
					|	FUN
					;

fields				:	field
					|	field
						COMMA
						fields
					;

field				:	unnBody
					|	ANY
						unnBody
					|	BAREID
						COLON
						unnBody
					|	BAREID
						COLON
						ANY
						unnBody
					;

mapArrow			:	WEAKARROW
					|	STRONGARROW
					;

// Type Terms

unnBody				:	unnCmp
					|	unnCmp
						PLUS
						unnBody
					;

unnCmp				:	typeId
					|	LCBRACE
						enumList
						RCBRACE
					;

typeId				:	BAREID
					|	QUALID
					;

enumList			:	enumCnst
					|	enumCnst
						COMMA
						enumList
					;

enumCnst			:	BAREID
					|	REAL
					|	FRAC
					|	str
					|	DIGITS
					|	MINUS 
						DIGITS
					|	QUALID
					|	DIGITS
						RANGE
						DIGITS
					|	MINUS
						DIGITS
						RANGE
						DIGITS
					|	DIGITS
						RANGE
						MINUS
						DIGITS
					|	MINUS
						DIGITS
						RANGE
						MINUS
						DIGITS
					;

// Facts, Rules, and Comprehensions

ruleItem			:	funcTermList
						DOT
					|	funcTermList
						RULE
						bodyList
						DOT
					;

compr				:	LCBRACE
						funcTermList
						comprRest
					;

comprRest			:	RCBRACE
					|	PIPE
						bodyList
						RCBRACE
					;

bodyList			:	body
					|	body
						SEMICOLON
						bodyList
					;

body				:	constraint
					|	constraint
						COMMA
						body
					;

// Terms and Constraints

constraint			:	funcTerm
					|	id
						IS
						funcTerm
					|	NO
						compr
					|	NO
						funcTerm
					|	NO
						id
						IS
						funcTerm
					|	funcTerm
						relOp
						funcTerm
					;

funcTermList		:	funcOrCompr
					|	funcOrCompr
						COMMA
						funcTermList
					;

funcOrCompr			:	funcTerm
					|	compr
					;

funcTerm			:	atom
					|	MINUS
						funcTerm
					|	funcTerm
						(MUL | DIV | MOD)
						funcTerm
					|	funcTerm
						(PLUS | MINUS)
						funcTerm
					|	id
						LPAREN
						funcTermList
						RPAREN
					|	QSTART
						quoteList
						QEND
					|	LPAREN
						funcTerm
						RPAREN
					;

quoteList			:	quoteItem
					|	quoteItem
						quoteList
					;

quoteItem			:	QRUN
					|	QESC
					|	UQSTART
						funcTerm
						UQEND
					;

atom				:	id
					|	constant
					;

id					:	BAREID
					|	QUALID
					;

constant			:	DIGITS
					|   MINUS DIGITS
					|   PLUS DIGITS
					|	REAL
					|	MINUS REAL
					|	PLUS REAL
					|	FRAC
					|	MINUS FRAC
					|	PLUS FRAC
					|	str
					;

unOp				:	MINUS
					;

binOp				:	MUL
					|	DIV
					|	MOD
					|	PLUS
					|	MINUS
					;

relOp				:	EQ
					|	NE
					|	LT
					|	LE
					|	GT
					|	GE
					|	COLON
					;

str					:	STRING
					|	STRINGMUL
					;

