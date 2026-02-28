import { createClient } from '@supabase/supabase-js'

// Вставь свои данные, когда они будут
const supabaseUrl = 'https://qyjddyfsesyepdzhypyo.supabase.co'
const supabaseKey = 'sb_publishable_3o33vKD_ozwJx4okaXTNcg_WjdB9fOI'

export const supabase = createClient(supabaseUrl, supabaseKey)