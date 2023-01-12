# -*- coding: utf-8 -*-

from pint import UnitRegistry

global ureg
ureg = UnitRegistry()
global Q
Q = ureg.Quantity

ureg.define('man = [man]')
ureg.define('man_hour = 1 man * hour = man_hours')
ureg.define('man_day = 8 * man_hour = man_days')
ureg.define('man_week = 5 * man_day = man_weeks')
ureg.define('man_month = 4 * man_week = man_months')
ureg.define('man_year = 52 * man_week = man_years')

ureg.define('item = [item]')

ureg.define('zloty = [zloty] = PLN=zl')
ureg.define('grosz = zloty / 100 = PLN/100=gr')

ureg.define('euro = [euro] = EUR')
ureg.define('euro_cent = euro / 100 = EUR/100')

ureg.define('us_dollar = [us_dollar] = USD = $')
ureg.define('us_cent = us_dollar / 100 = ¢')

ureg.define('au_dollar = [au_dollar] = AUD')
ureg.define('au_cent = au_dollar / 100 = AUD/100')

global minute
minute = ureg.minute

global man
man = ureg.man
