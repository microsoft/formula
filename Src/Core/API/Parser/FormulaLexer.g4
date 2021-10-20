lexer grammar FormulaLexer;

DOMAIN:		'domain' ;
MODEL:		'model' ;
TRANSFORM:	'transform' ;
SYSTEM:		'system' ;
MACHINE:	'machine' ;
PARTIAL:	'partial' ;

ENSURES:	'ensures' ;
REQUIRES:	'requires' ;
CONFORMS:	'conforms' ;

LCBRACE:	'{' ;
RCBRACE:	'}' ;

LPAREN:		'(' ;
RPAREN:		')' ;

LSBRACE:	'[' ;
RSBRACE:	']' ;

INCLUDES:	'includes' ;
EXTENDS:	'extends' ;
OF:			'of' ;
RETURNS:	'returns' ;

AT:			'at' ;
COLON:		':' ;

RENAMES:	'::' ;
RANGE:		'..' ;
SOME:		'some' ;
ATLEAST:	'atleast' ;
ATMOST:		'atmost' ;
INITIALLY:	'initially' ;
NEXT:		'next' ;
PROPERTY:	'property' ;
BOOT:		'boot' ;

EQ:			'=' ;
TYPEDEF:	'::=' ;
RULE:		':-' ;
PIPE:		'|' ;

DOT:		'.' ;

SEMICOLON:	';' ;

COMMA:		',' ;

NO:			'no' ;
IS:			'is' ;
WEAKARROW:	'->' ;
STRONGARROW:'=>' ;

NEW:		'new' ;
INJ:		'inj' ;
BIJ:		'bij' ;
SUR:		'sur' ;
FUN:		'fun' ;
ANY:		'any' ;
SUB:		'sub' ;

BAREID:		([%]?BID | TID) ;
QUALID:		(BID[.])+(BID|TID|SID) | [%][A-Za-z_]([A-Za-z_0-9]*[']*)([.][A-Za-z_]([A-Za-z_0-9]*[']*))+ ;

DIGITS:		[0-9]+ ;
REAL:		[0-9]+'.'[0-9]+ ;
FRAC:		[0-9]+'/'[-+]?[0]*[1-9][0-9]* ;

STRING:		'"' (ESC | ~([\r\n]))*? '"' ;
STRINGMUL:	'\'"' (MULESC | .)*? '"\'' ;

NE:			'!=' ;
LT:			'<' ;
GT:			'>' ;
GE:			'>=' ;
LE:			'<=' ;

PLUS:		'+' ;
MINUS:		'-' ;
MOD:		'%' ;
DIV:		'/' ;
MUL:		'*' ;

UQEND:		'$'	-> popMode ;

COMMENT:	(CMNTSTART .*? CMNTEND) -> skip ;
ALTCOMMENT:	(CMNTALT)(NONLF)([\n\r]?) -> skip ;
WS:			[ \t\r\n]+ -> skip ;

QSTART:		'`' -> pushMode(QUOTING) ;

fragment LETTER			:	[a-zA-Z] ;
fragment DIGIT			:	[0-9] ;
fragment ESC			:	'\\"' | '\\\\' | '\\r' | '\\n' | '\\t' ;
fragment MULESC			:	'\'\'""' | '""\'\'' ;
fragment BID			:	[A-Za-z_]([A-Za-z_0-9]*[']*) ;
fragment TID			:	[#][A-Za-z_]([A-Za-z_0-9]*[']*)([[][0-9]+[\]])? ;
fragment SID			:	[%][A-Za-z_]([A-Za-z_0-9]*[']*)([.][A-Za-z_]([A-Za-z_0-9]*[']*))* ;
fragment CMNTSTART		:	'/*' ;
fragment CMNTEND		:	'*/' ;
fragment CMNTALT		:	'//' ;
fragment NONLF			:	~([\r\n])* ;

mode QUOTING;

QRUN:		(NONQCNTRCHARS)('\r\n'?|[\n\r]?) ;
QESC:		('``' | '$$') ;
QEND:		'`' -> popMode ;
UQSTART:	'$'	-> pushMode(DEFAULT_MODE);

fragment NONQCNTRCHARS	:	~([`$\n\r])* ;
